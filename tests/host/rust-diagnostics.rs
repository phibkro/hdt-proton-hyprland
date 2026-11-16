use base64::{Engine, engine::general_purpose::STANDARD};
use hdt_host::{
    journal::{DiscoveryRoot, From, GameLog, InputJournal, Source},
    trace,
};
use serde_json::{Value, json};
use std::{
    fs::{self, OpenOptions},
    io::Write,
    os::unix::fs::{PermissionsExt, symlink},
    path::Path,
    process::{Child, Command, Stdio},
    time::{Duration, Instant},
};

fn rows(root: &Path) -> Vec<Value> {
    fs::read_to_string(root.join("journal.jsonl"))
        .unwrap()
        .split_inclusive('\n')
        .filter(|line| line.ends_with('\n'))
        .map(|line| serde_json::from_str(line).unwrap())
        .collect()
}
fn bytes(rows: &[Value], source: &str, generation: Option<u64>) -> Vec<u8> {
    rows.iter()
        .filter(|row| {
            row["kind"] == "source.bytes"
                && row["data"]["source"] == source
                && generation.is_none_or(|g| row["data"]["generation"] == g)
        })
        .flat_map(|row| {
            STANDARD
                .decode(row["data"]["base64"].as_str().unwrap())
                .unwrap()
        })
        .collect()
}
fn append(path: &Path, bytes: &[u8]) {
    OpenOptions::new()
        .append(true)
        .open(path)
        .unwrap()
        .write_all(bytes)
        .unwrap();
}
fn journal(root: &Path, source: &Path, from: From, capacity: u64) -> InputJournal {
    InputJournal::new(
        root.to_owned(),
        vec![Source {
            name: "hdt".into(),
            path: source.to_owned(),
        }],
        from,
        capacity,
        vec![],
    )
    .unwrap()
}
#[test]
fn exact_bytes_rotation_rewrite_truncation_and_private_evidence() {
    let temp = tempfile::tempdir().unwrap();
    let source = temp.path().join("source");
    let output = temp.path().join("output");
    let content = "Info|Game ended\r\n雪\n".as_bytes();
    fs::write(&source, &content[..content.len() - 2]).unwrap();
    let mut collector = journal(&output, &source, From::Start, 1_048_576);
    collector.start().unwrap();
    collector.poll().unwrap();
    append(&source, &content[content.len() - 2..]);
    collector.poll().unwrap();
    fs::rename(&source, temp.path().join("old")).unwrap();
    fs::write(&source, b"new session\n").unwrap();
    collector.poll().unwrap();
    fs::write(&source, b"longer rewritten body\n").unwrap();
    collector.poll().unwrap();
    fs::write(&source, b"x").unwrap();
    collector.poll().unwrap();
    collector.close("test").unwrap();
    let records = rows(&output);
    assert_eq!(bytes(&records, "hdt", Some(0)), content);
    assert_eq!(bytes(&records, "hdt", Some(1)), b"new session\n");
    assert_eq!(bytes(&records, "hdt", Some(2)), b"longer rewritten body\n");
    assert_eq!(bytes(&records, "hdt", Some(3)), b"x");
    let resets: Vec<_> = records
        .iter()
        .filter(|r| r["data"]["continuity"] == "unknown")
        .map(|r| r["kind"].as_str().unwrap())
        .collect();
    assert_eq!(
        resets,
        ["source.replaced", "source.rewritten", "source.truncated"]
    );
    assert_eq!(
        fs::metadata(&output).unwrap().permissions().mode() & 0o077,
        0
    );
    assert_eq!(
        fs::metadata(output.join("journal.jsonl"))
            .unwrap()
            .permissions()
            .mode()
            & 0o077,
        0
    );
    assert_eq!(records.last().unwrap()["kind"], "journal.stop");
    assert!(collector.poll().is_err());
    collector.close("again").unwrap();
}
#[test]
fn bounded_chunks_drain_old_descriptor_before_switching_generation() {
    let temp = tempfile::tempdir().unwrap();
    let source = temp.path().join("source");
    let output = temp.path().join("output");
    let old = vec![0xff; 100_000];
    fs::write(&source, &old).unwrap();
    let mut collector = journal(&output, &source, From::Start, 1_048_576);
    collector.start().unwrap();
    collector.poll().unwrap();
    fs::rename(&source, temp.path().join("old")).unwrap();
    fs::write(&source, b"next").unwrap();
    collector.poll().unwrap();
    collector.poll().unwrap();
    collector.poll().unwrap();
    collector.close("test").unwrap();
    let records = rows(&output);
    assert_eq!(bytes(&records, "hdt", Some(0)), old);
    assert_eq!(bytes(&records, "hdt", Some(1)), b"next");
    assert!(
        records
            .iter()
            .filter(|r| r["kind"] == "source.bytes")
            .all(|r| STANDARD
                .decode(r["data"]["base64"].as_str().unwrap())
                .unwrap()
                .len()
                <= 32768)
    );
}
#[test]
fn capacity_failure_keeps_evidence_and_reserved_stop_record() {
    let temp = tempfile::tempdir().unwrap();
    let source = temp.path().join("source");
    let output = temp.path().join("output");
    fs::write(&source, vec![65; 100_000]).unwrap();
    let mut collector = journal(&output, &source, From::Start, 65_536);
    collector.start().unwrap();
    collector.poll().unwrap();
    assert!(
        collector
            .poll()
            .unwrap_err()
            .to_string()
            .contains("capacity exhausted")
    );
    collector.close("capacity exhausted").unwrap();
    let records = rows(&output);
    assert_eq!(bytes(&records, "hdt", Some(0)), vec![65; 32768]);
    assert_eq!(
        records.last().unwrap()["data"]["reason"],
        "capacity exhausted"
    );
    assert!(fs::metadata(output.join("journal.jsonl")).unwrap().len() <= 65_536);
}
#[test]
fn rejects_existing_outputs_aliases_symlinks_and_invalid_names() {
    let temp = tempfile::tempdir().unwrap();
    let source = temp.path().join("source");
    fs::write(&source, b"evidence").unwrap();
    let output = temp.path().join("output");
    fs::create_dir(&output).unwrap();
    assert!(
        journal(&output, &source, From::Start, 65536)
            .start()
            .is_err()
    );
    let link = temp.path().join("link");
    symlink(&source, &link).unwrap();
    let mut collector = journal(&temp.path().join("linked"), &link, From::Start, 65536);
    collector.start().unwrap();
    assert!(
        collector
            .poll()
            .unwrap_err()
            .to_string()
            .contains("regular file")
    );
    collector.close("rejected").unwrap();
    assert!(
        InputJournal::new(
            temp.path().join("invalid"),
            vec![Source {
                name: "Bad Name".into(),
                path: source.clone()
            }],
            From::Start,
            65536,
            vec![]
        )
        .is_err()
    );
    assert!(
        InputJournal::new(
            temp.path().join("invalid"),
            vec![
                Source {
                    name: "hdt".into(),
                    path: source.clone()
                },
                Source {
                    name: "other".into(),
                    path: source
                }
            ],
            From::Start,
            65536,
            vec![]
        )
        .is_err()
    );
    let self_output = temp.path().join("self");
    assert!(
        InputJournal::new(
            self_output.clone(),
            vec![Source {
                name: "self".into(),
                path: self_output.join("journal.jsonl")
            }],
            From::Start,
            65536,
            vec![]
        )
        .is_err()
    );
}
#[test]
fn end_policy_skips_history_but_captures_late_sources_and_sessions() {
    let temp = tempfile::tempdir().unwrap();
    let source = temp.path().join("source");
    let missing = temp.path().join("missing");
    let output = temp.path().join("output");
    let root = temp.path().join("recorders");
    fs::create_dir(&root).unwrap();
    let session = |id: &str| root.join(format!("flight-recorder-12-{}", id.repeat(32)));
    fs::create_dir(session("a")).unwrap();
    fs::write(session("a").join("events.jsonl"), b"history").unwrap();
    fs::write(&source, b"history").unwrap();
    let mut collector = InputJournal::new(
        output.clone(),
        vec![
            Source {
                name: "hdt".into(),
                path: source.clone(),
            },
            Source {
                name: "late".into(),
                path: missing.clone(),
            },
        ],
        From::End,
        1_048_576,
        vec![DiscoveryRoot::Recorder(root.clone())],
    )
    .unwrap();
    collector.start().unwrap();
    collector.poll().unwrap();
    append(&source, b"next");
    fs::write(&missing, b"first").unwrap();
    append(&session("a").join("events.jsonl"), b"tail");
    for id in ["b", "c"] {
        fs::create_dir(session(id)).unwrap();
        fs::write(session(id).join("events.jsonl"), format!("{id} first")).unwrap();
    }
    collector.poll().unwrap();
    collector.close("test").unwrap();
    let records = rows(&output);
    assert_eq!(bytes(&records, "hdt", None), b"next");
    assert_eq!(bytes(&records, "late", None), b"first");
    let discovered: Vec<_> = records
        .iter()
        .filter(|r| r["kind"] == "recorder.discovered")
        .collect();
    assert_eq!(discovered.len(), 3);
    assert_eq!(discovered[0]["data"]["from"], "end");
    assert_eq!(discovered[1]["data"]["from"], "start");
    for (id, expected) in [("a", "tail"), ("b", "b first"), ("c", "c first")] {
        assert_eq!(
            bytes(&records, &format!("recorder-12-{}", id.repeat(32)), None),
            expected.as_bytes()
        );
    }
}
#[test]
fn root_can_appear_later_and_session_capacity_is_explicit() {
    let temp = tempfile::tempdir().unwrap();
    let root = temp.path().join("recorders");
    let output = temp.path().join("output");
    let mut collector = InputJournal::new(
        output.clone(),
        vec![],
        From::End,
        1_048_576,
        vec![DiscoveryRoot::Recorder(root.clone())],
    )
    .unwrap();
    collector.start().unwrap();
    collector.poll().unwrap();
    fs::create_dir(&root).unwrap();
    for pid in 1..=128 {
        fs::create_dir(root.join(format!("flight-recorder-{pid}-{}", "f".repeat(32)))).unwrap();
    }
    collector.poll().unwrap();
    let session = root.join(format!("flight-recorder-1-{}", "f".repeat(32)));
    fs::write(session.join("events.jsonl"), b"first event").unwrap();
    collector.poll().unwrap();
    fs::create_dir(root.join(format!("flight-recorder-129-{}", "f".repeat(32)))).unwrap();
    assert!(
        collector
            .poll()
            .unwrap_err()
            .to_string()
            .contains("session capacity exhausted")
    );
    collector.close("capacity").unwrap();
    let records = rows(&output);
    assert_eq!(
        records
            .iter()
            .filter(|r| r["kind"] == "recorder.discovered")
            .count(),
        128
    );
    assert_eq!(
        records
            .iter()
            .filter(|r| r["kind"] == "recorder.root.unavailable")
            .count(),
        1
    );
    assert_eq!(
        records
            .iter()
            .filter(|r| r["kind"] == "recorder.root.available")
            .count(),
        1
    );
    assert_eq!(
        bytes(&records, &format!("recorder-1-{}", "f".repeat(32)), None),
        b"first event"
    );
}
#[test]
fn recorder_discovery_refuses_directory_links_and_explicit_aliases() {
    let temp = tempfile::tempdir().unwrap();
    let root = temp.path().join("recorders");
    fs::create_dir(&root).unwrap();
    let session = root.join(format!("flight-recorder-42-{}", "e".repeat(32)));
    fs::create_dir(&session).unwrap();
    let mut collector = InputJournal::new(
        temp.path().join("alias"),
        vec![Source {
            name: "alias".into(),
            path: session.join("events.jsonl"),
        }],
        From::Start,
        65536,
        vec![DiscoveryRoot::Recorder(root.clone())],
    )
    .unwrap();
    assert!(
        collector
            .start()
            .unwrap_err()
            .to_string()
            .contains("duplicates")
    );
    collector.close("alias").unwrap();
    fs::remove_dir(&session).unwrap();
    symlink(temp.path(), &session).unwrap();
    let mut collector = InputJournal::new(
        temp.path().join("linked"),
        vec![],
        From::Start,
        65536,
        vec![DiscoveryRoot::Recorder(root)],
    )
    .unwrap();
    assert!(
        collector
            .start()
            .unwrap_err()
            .to_string()
            .contains("not a symlink")
    );
    collector.close("linked").unwrap();
}
fn event(change: Value) -> String {
    let mut value = json!({"schema":1,"session":"test","seq":1,"tsUS":3500000,"thread":2,"name":"ui.dispatch","data":{"durationMS":3000}});
    for (key, value2) in change.as_object().unwrap() {
        value[key] = value2.clone();
    }
    value.to_string()
}
#[test]
fn trace_duration_sessions_validation_and_duplicate_identity() {
    let first = event(json!({}));
    let second = event(json!({"session":"next","tsUS":1000,"name":"session.start","data":{}}));
    let events = trace::trace_events([first.as_str(), second.as_str()]).unwrap();
    assert_eq!(events[0]["ph"], "X");
    assert_eq!(events[0]["ts"].as_f64(), Some(500000.0));
    assert_eq!(events[0]["dur"].as_f64(), Some(3000000.0));
    assert_eq!(events[1]["pid"], 2);
    assert!(
        trace::trace_events([first.as_str(), first.as_str()])
            .unwrap_err()
            .to_string()
            .contains("Duplicate event")
    );
    for change in [
        json!({"schema":2}),
        json!({"tsUS":-1}),
        json!({"tsUS":9007199254740992_u64}),
        json!({"thread":0}),
        json!({"seq":0}),
        json!({"seq":1.5}),
        json!({"data":[]}),
        json!({"data":{"durationMS":4000}}),
        json!({"data":{"durationMS":null}}),
    ] {
        assert!(trace::trace_events([event(change).as_str()]).is_err());
    }
}
#[test]
fn trace_cli_creates_private_output_only_after_validation() {
    let temp = tempfile::tempdir().unwrap();
    let input = temp.path().join("events.jsonl");
    let output = temp.path().join("trace.json");
    let text = event(json!({}));
    fs::write(&input, &text).unwrap();
    trace::run(&[
        output.clone().into_os_string(),
        input.clone().into_os_string(),
    ])
    .unwrap();
    let original = fs::read(&output).unwrap();
    assert_eq!(
        fs::metadata(&output).unwrap().permissions().mode() & 0o077,
        0
    );
    assert!(
        trace::run(&[
            output.clone().into_os_string(),
            input.clone().into_os_string()
        ])
        .is_err()
    );
    assert_eq!(fs::read(&output).unwrap(), original);
    let duplicate = temp.path().join("duplicate.json");
    assert!(
        trace::run(&[
            duplicate.clone().into_os_string(),
            input.clone().into_os_string(),
            input.clone().into_os_string()
        ])
        .is_err()
    );
    assert!(!duplicate.exists());
    assert_eq!(fs::read_to_string(input).unwrap(), text);
}

struct OwnedChild(Child);
impl Drop for OwnedChild {
    fn drop(&mut self) {
        let _ = self.0.kill();
        let _ = self.0.wait();
    }
}
fn wait_for(mut predicate: impl FnMut() -> bool) {
    let start = Instant::now();
    while !predicate() {
        assert!(
            start.elapsed() < Duration::from_secs(6),
            "timed out waiting for owned process evidence"
        );
        std::thread::sleep(Duration::from_millis(25));
    }
}
#[test]
fn collector_continues_when_independent_producer_stops_dies_and_restarts() {
    use nix::{
        sys::signal::{Signal, kill},
        unistd::Pid,
    };
    let temp = tempfile::tempdir().unwrap();
    let source = temp.path().join("game");
    let hdt = temp.path().join("hdt");
    let output = temp.path().join("output");
    let root = temp.path().join("recorders");
    fs::write(&source, b"").unwrap();
    fs::write(&hdt, b"").unwrap();
    fs::create_dir(&root).unwrap();
    let mut collector = InputJournal::new(
        output.clone(),
        vec![
            Source {
                name: "game".into(),
                path: source.clone(),
            },
            Source {
                name: "hdt".into(),
                path: hdt.clone(),
            },
        ],
        From::Start,
        1_048_576,
        vec![DiscoveryRoot::Recorder(root.clone())],
    )
    .unwrap();
    collector.start().unwrap();
    let first = root.join(format!("flight-recorder-12-{}", "a".repeat(32)));
    let mut producer=OwnedChild(Command::new("sh").args(["-c","mkdir \"$1\"; printf 'first recorder\\n' > \"$1/events.jsonl\"; printf 'running\\n' >> \"$2\"; exec sleep 30","producer"]).arg(&first).arg(&hdt).stdout(Stdio::null()).stderr(Stdio::null()).spawn().unwrap());
    wait_for(|| fs::read_to_string(&hdt).unwrap().contains("running"));
    collector.poll().unwrap();
    kill(Pid::from_raw(producer.0.id() as i32), Signal::SIGSTOP).unwrap();
    append(&source, b"game after stall\n");
    collector.poll().unwrap();
    assert!(
        String::from_utf8(bytes(&rows(&output), "game", None))
            .unwrap()
            .contains("after stall")
    );
    producer.0.kill().unwrap();
    producer.0.wait().unwrap();
    append(&source, b"game after exit\n");
    let second = root.join(format!("flight-recorder-12-{}", "b".repeat(32)));
    let status=Command::new("sh").args(["-c","mkdir \"$1\"; printf 'second recorder\\n' > \"$1/events.jsonl\"; printf 'restarted\\n' >> \"$2\"","producer"]).arg(second).arg(hdt).status().unwrap();
    assert!(status.success());
    collector.poll().unwrap();
    collector.close("test").unwrap();
    let records = rows(&output);
    assert_eq!(
        bytes(&records, "game", None),
        b"game after stall\ngame after exit\n"
    );
    assert_eq!(bytes(&records, "hdt", None), b"running\nrestarted\n");
    assert_eq!(
        records
            .iter()
            .filter(|r| r["kind"] == "recorder.discovered")
            .count(),
        2
    );
}

#[test]
fn collector_cli_flushes_on_sigterm_and_refuses_adopting_prior_capture() {
    use nix::{
        sys::signal::{Signal, kill},
        unistd::Pid,
    };
    let temp = tempfile::tempdir().unwrap();
    let source = temp.path().join("source");
    let output = temp.path().join("output");
    fs::write(&source, b"initial bytes\n").unwrap();
    let mut child = OwnedChild(
        Command::new(env!("CARGO_BIN_EXE_hdt-host"))
            .args(["observe", "--output"])
            .arg(&output)
            .args(["--from", "start", "--max-bytes", "1048576", "--source"])
            .arg(format!("hdt={}", source.display()))
            .stdout(Stdio::null())
            .stderr(Stdio::piped())
            .spawn()
            .unwrap(),
    );
    wait_for(|| {
        output.join("journal.jsonl").exists()
            && bytes(&rows(&output), "hdt", None) == b"initial bytes\n"
    });
    append(&source, b"later bytes\n");
    wait_for(|| bytes(&rows(&output), "hdt", None).ends_with(b"later bytes\n"));
    kill(Pid::from_raw(child.0.id() as i32), Signal::SIGTERM).unwrap();
    assert!(child.0.wait().unwrap().success());
    let records = rows(&output);
    assert_eq!(records.last().unwrap()["data"]["reason"], "signal");
    let prior = fs::read(output.join("journal.jsonl")).unwrap();
    let status = Command::new(env!("CARGO_BIN_EXE_hdt-host"))
        .args(["observe", "--output"])
        .arg(&output)
        .args(["--from", "start", "--max-bytes", "65536", "--source"])
        .arg(format!("hdt={}", source.display()))
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .status()
        .unwrap();
    assert!(!status.success());
    assert_eq!(fs::read(output.join("journal.jsonl")).unwrap(), prior);
}

fn game_collector(root: &Path, output: &Path, capacity: u64) -> OwnedChild {
    OwnedChild(
        Command::new(env!("CARGO_BIN_EXE_hdt-host"))
            .args(["observe", "--output"])
            .arg(output)
            .args(["--from", "start", "--max-bytes"])
            .arg(capacity.to_string())
            .arg("--game-log-root")
            .arg(root)
            .args(["--game-log", "Power.log", "--game-log", "LoadingScreen.log"])
            .stdout(Stdio::null())
            .stderr(Stdio::piped())
            .spawn()
            .unwrap(),
    )
}
fn game_source(records: &[Value], session: &str, filename: &str) -> String {
    records
        .iter()
        .find(|row| {
            row["kind"] == "game.discovered"
                && row["data"]["session"] == session
                && Path::new(row["data"]["path"].as_str().unwrap())
                    .file_name()
                    .unwrap()
                    == filename
        })
        .unwrap()["data"]["name"]
        .as_str()
        .unwrap()
        .to_owned()
}
fn stop(child: &mut OwnedChild) {
    nix::sys::signal::kill(
        nix::unistd::Pid::from_raw(child.0.id() as i32),
        nix::sys::signal::Signal::SIGTERM,
    )
    .unwrap();
    wait_for(|| child.0.try_wait().unwrap().is_some());
    assert!(child.0.wait().unwrap().success());
}
#[test]
fn game_log_cli_captures_rollover_generations_root_recovery_and_new_capture() {
    let temp = tempfile::tempdir().unwrap();
    let root = temp.path().join("logs");
    let output = temp.path().join("output");
    let mut child = game_collector(&root, &output, 2_097_152);
    wait_for(|| {
        output.join("journal.jsonl").exists()
            && rows(&output)
                .iter()
                .any(|r| r["kind"] == "game.root.unavailable")
    });
    let first = root.join("Hearthstone_first");
    fs::create_dir_all(&first).unwrap();
    let power = first.join("Power.log");
    let original = b"first\0\xff\r\npartial";
    fs::write(&power, original).unwrap();
    fs::write(first.join("unselected.log"), b"must never be captured").unwrap();
    wait_for(|| rows(&output).iter().any(|r| r["kind"] == "source.bytes"));
    let source = game_source(&rows(&output), "Hearthstone_first", "Power.log");
    wait_for(|| bytes(&rows(&output), &source, Some(0)) == original);
    append(&power, b" tail\n");
    let mut expected = original.to_vec();
    expected.extend_from_slice(b" tail\n");
    wait_for(|| bytes(&rows(&output), &source, Some(0)) == expected);
    // A new producer session must not abandon unread bytes from the old one.
    let large = vec![b'x'; 100_000];
    append(&power, &large);
    expected.extend_from_slice(&large);
    let second = root.join("Hearthstone_second");
    fs::create_dir(&second).unwrap();
    fs::write(second.join("Power.log"), b"producer restart").unwrap();
    fs::write(first.join("LoadingScreen.log"), b"late file").unwrap();
    wait_for(|| {
        rows(&output)
            .iter()
            .filter(|r| r["kind"] == "game.discovered")
            .count()
            == 4
    });
    let second_source = game_source(&rows(&output), "Hearthstone_second", "Power.log");
    let late_source = game_source(&rows(&output), "Hearthstone_first", "LoadingScreen.log");
    wait_for(|| {
        bytes(&rows(&output), &source, Some(0)) == expected
            && bytes(&rows(&output), &second_source, Some(0)) == b"producer restart"
            && bytes(&rows(&output), &late_source, Some(0)) == b"late file"
    });
    assert_eq!(fs::read(&power).unwrap(), expected);
    fs::write(&power, b"truncated").unwrap();
    wait_for(|| bytes(&rows(&output), &source, Some(1)) == b"truncated");
    fs::rename(&power, first.join("rotated")).unwrap();
    fs::write(&power, b"replacement").unwrap();
    wait_for(|| bytes(&rows(&output), &source, Some(2)) == b"replacement");
    let hidden = temp.path().join("temporarily-hidden");
    fs::rename(&root, &hidden).unwrap();
    wait_for(|| {
        rows(&output)
            .iter()
            .filter(|r| r["kind"] == "game.root.unavailable")
            .count()
            == 2
    });
    fs::rename(&hidden, &root).unwrap();
    wait_for(|| {
        rows(&output)
            .iter()
            .filter(|r| r["kind"] == "game.root.available")
            .count()
            == 2
    });
    append(&power, b" after recovery");
    wait_for(|| bytes(&rows(&output), &source, None).ends_with(b"replacement after recovery"));
    stop(&mut child);
    let records = rows(&output);
    assert_eq!(records.last().unwrap()["data"]["reason"], "signal");
    assert!(
        records
            .iter()
            .any(|r| r["kind"] == "source.truncated" && r["data"]["source"] == source)
    );
    assert!(
        records
            .iter()
            .any(|r| r["kind"] == "source.replaced" && r["data"]["source"] == source)
    );
    assert!(!records.iter().any(|r| {
        r["data"]["path"]
            .as_str()
            .is_some_and(|p| p.ends_with("unselected.log"))
    }));
    assert_eq!(fs::read(&power).unwrap(), b"replacement after recovery");
    assert_eq!(fs::read(first.join("rotated")).unwrap(), b"truncated");
    assert_eq!(
        fs::metadata(&output).unwrap().permissions().mode() & 0o077,
        0
    );
    assert_eq!(
        fs::metadata(output.join("journal.jsonl"))
            .unwrap()
            .permissions()
            .mode()
            & 0o077,
        0
    );
    // A collector restart is a new capture with explicit offset policy, never implicit resume.
    let next = temp.path().join("next");
    let mut restarted = game_collector(&root, &next, 2_097_152);
    wait_for(|| {
        next.join("journal.jsonl").exists()
            && bytes(&rows(&next), &source, Some(0)) == b"replacement after recovery"
    });
    stop(&mut restarted);
    let mut offsets = std::collections::HashMap::new();
    for row in records.iter().filter(|r| r["kind"] == "source.bytes") {
        let data = &row["data"];
        let key = (
            data["source"].as_str().unwrap(),
            data["generation"].as_u64().unwrap(),
        );
        let entry = offsets
            .entry(key)
            .or_insert((0u64, data["identity"].as_str().unwrap()));
        assert_eq!(data["offset"].as_u64().unwrap(), entry.0);
        assert_eq!(data["identity"].as_str().unwrap(), entry.1);
        entry.0 += STANDARD
            .decode(data["base64"].as_str().unwrap())
            .unwrap()
            .len() as u64;
    }
}
#[test]
fn game_log_cli_rejects_links_and_reports_capacity_failures() {
    for fault in [
        "root",
        "ancestor",
        "session",
        "file",
        "session-capacity",
        "scan-capacity",
        "byte-capacity",
    ] {
        let temp = tempfile::tempdir().unwrap();
        let root = temp.path().join("logs");
        fs::create_dir(&root).unwrap();
        let external = temp.path().join("outside");
        fs::create_dir(&external).unwrap();
        fs::write(external.join("Power.log"), b"outside secret fixture").unwrap();
        let session = root.join("session");
        fs::create_dir(&session).unwrap();
        match fault {
            "root" => {
                fs::remove_dir(&session).unwrap();
                fs::remove_dir(&root).unwrap();
                symlink(&external, &root).unwrap();
            }
            "ancestor" => {
                fs::remove_dir(&session).unwrap();
                fs::remove_dir(&root).unwrap();
                symlink(&external, &root).unwrap();
            }
            "session" => {
                fs::remove_dir(&session).unwrap();
                symlink(&external, &session).unwrap();
            }
            "file" => {
                symlink(external.join("Power.log"), session.join("Power.log")).unwrap();
            }
            "session-capacity" => {
                for n in 0..128 {
                    fs::create_dir(root.join(format!("session-{n}"))).unwrap();
                }
            }
            "scan-capacity" => {
                for n in 0..4096 {
                    fs::write(root.join(format!("ignored.{n}")), b"").unwrap();
                }
            }
            "byte-capacity" => {
                fs::write(session.join("Power.log"), vec![65; 100_000]).unwrap();
            }
            _ => unreachable!(),
        }
        let selected = if fault == "ancestor" {
            root.join("nested")
        } else {
            root.clone()
        };
        let output = temp.path().join("output");
        let mut child = game_collector(
            &selected,
            &output,
            if fault == "byte-capacity" {
                65536
            } else {
                4_194_304
            },
        );
        wait_for(|| child.0.try_wait().unwrap().is_some());
        assert!(!child.0.wait().unwrap().success(), "{fault}");
        let records = rows(&output);
        assert_eq!(records.last().unwrap()["kind"], "journal.stop", "{fault}");
        assert!(
            records.last().unwrap()["data"]["reason"]
                .as_str()
                .unwrap()
                .contains(if fault.ends_with("capacity") {
                    "capacity exhausted"
                } else if fault == "file" {
                    "regular file"
                } else {
                    "symlink"
                }),
            "{fault}: {records:?}"
        );
        assert!(
            !records
                .iter()
                .filter(|r| r["kind"] == "source.bytes")
                .any(|r| STANDARD
                    .decode(r["data"]["base64"].as_str().unwrap())
                    .unwrap()
                    .windows(7)
                    .any(|w| w == b"outside"))
        );
        assert_eq!(
            fs::read(external.join("Power.log")).unwrap(),
            b"outside secret fixture"
        );
    }
}
#[test]
fn game_log_selection_is_explicit_and_closed_and_end_policy_preserves_late_input() {
    let temp = tempfile::tempdir().unwrap();
    let root = temp.path().join("logs");
    fs::create_dir_all(root.join("session")).unwrap();
    let output = temp.path().join("output");
    for extra in [
        vec!["--game-log-root", "relative", "--game-log", "Power.log"],
        vec!["--game-log-root", root.to_str().unwrap()],
        vec!["--game-log", "Power.log"],
        vec![
            "--game-log-root",
            root.to_str().unwrap(),
            "--game-log",
            "../Power.log",
        ],
        vec![
            "--game-log-root",
            root.to_str().unwrap(),
            "--game-log",
            "Power.log",
            "--game-log",
            "Power.log",
        ],
    ] {
        let status = Command::new(env!("CARGO_BIN_EXE_hdt-host"))
            .args(["observe", "--output"])
            .arg(&output)
            .args(["--from", "start", "--max-bytes", "65536"])
            .args(extra)
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .status()
            .unwrap();
        assert!(!status.success());
        assert!(!output.exists());
    }
    fs::write(root.join("session/Power.log"), b"history").unwrap();
    let mut collector = InputJournal::new(
        output.clone(),
        vec![],
        From::End,
        1_048_576,
        vec![DiscoveryRoot::GameLogs {
            root: root.clone(),
            logs: vec![GameLog::Power, GameLog::Arena],
        }],
    )
    .unwrap();
    collector.start().unwrap();
    collector.poll().unwrap();
    append(&root.join("session/Power.log"), b"tail");
    fs::write(root.join("session/Arena.log"), b"late").unwrap();
    fs::create_dir(root.join("new-session")).unwrap();
    fs::write(root.join("new-session/Power.log"), b"new producer").unwrap();
    collector.poll().unwrap();
    collector.close("test").unwrap();
    let records = rows(&output);
    assert_eq!(
        bytes(
            &records,
            &game_source(&records, "session", "Power.log"),
            None
        ),
        b"tail"
    );
    assert_eq!(
        bytes(
            &records,
            &game_source(&records, "session", "Arena.log"),
            None
        ),
        b"late"
    );
    assert_eq!(
        bytes(
            &records,
            &game_source(&records, "new-session", "Power.log"),
            None
        ),
        b"new producer"
    );
}

#[test]
fn startup_end_boundary_captures_pre_poll_arrivals_and_replacement_recovery() {
    for transition in ["append", "replace", "disappear"] {
        let temp = tempfile::tempdir().unwrap();
        let root = temp.path().join("logs");
        let session = root.join("session");
        fs::create_dir_all(&session).unwrap();
        let power = session.join("Power.log");
        let arena = session.join("Arena.log");
        fs::write(&power, b"history").unwrap();
        let output = temp.path().join("output");
        let mut collector = InputJournal::new(
            output.clone(),
            vec![],
            From::End,
            1_048_576,
            vec![DiscoveryRoot::GameLogs {
                root,
                logs: vec![GameLog::Power, GameLog::Arena],
            }],
        )
        .unwrap();
        collector.start().unwrap();
        // No regular poll occurs before this late file appears.
        fs::write(&arena, b"late before first poll").unwrap();
        match transition {
            "append" => append(&power, b"tail"),
            "replace" => {
                fs::rename(&power, session.join("retired")).unwrap();
                fs::write(&power, b"replacement").unwrap();
            }
            "disappear" => fs::remove_file(&power).unwrap(),
            _ => unreachable!(),
        }
        collector.poll().unwrap();
        if transition == "disappear" {
            fs::write(&power, b"replacement").unwrap();
            collector.poll().unwrap();
        }
        collector.close("test").unwrap();
        let records = rows(&output);
        let power_source = game_source(&records, "session", "Power.log");
        let arena_source = game_source(&records, "session", "Arena.log");
        assert_eq!(
            bytes(&records, &arena_source, Some(0)),
            b"late before first poll"
        );
        assert_eq!(
            bytes(&records, &power_source, None),
            if transition == "append" {
                b"tail".as_slice()
            } else {
                b"replacement".as_slice()
            },
            "{transition}: existing history must stay skipped and new input retained"
        );
        if transition != "append" {
            assert!(bytes(&records, &power_source, Some(0)).is_empty());
            assert_eq!(bytes(&records, &power_source, Some(1)), b"replacement");
        }
        assert_eq!(fs::read(&arena).unwrap(), b"late before first poll");
        if transition == "replace" {
            assert_eq!(fs::read(session.join("retired")).unwrap(), b"history");
        }
    }
}

#[test]
fn recorder_and_game_roots_share_capture_and_reject_an_ancestor_replaced_by_a_link() {
    let temp = tempfile::tempdir().unwrap();
    let root = temp.path().join("logs");
    let session = root.join("session");
    fs::create_dir_all(&session).unwrap();
    fs::write(session.join("Power.log"), b"game bytes").unwrap();
    let recorders = temp.path().join("recorders");
    let recorder = recorders.join(format!("flight-recorder-42-{}", "a".repeat(32)));
    fs::create_dir_all(&recorder).unwrap();
    fs::write(recorder.join("events.jsonl"), b"recorder bytes").unwrap();
    // The documented recorder journey keeps captures beside flight-recorder-*.
    let output = recorders.join("capture");
    let mut collector = InputJournal::new(
        output.clone(),
        vec![],
        From::Start,
        1_048_576,
        vec![
            DiscoveryRoot::Recorder(recorders),
            DiscoveryRoot::GameLogs {
                root: root.clone(),
                logs: vec![GameLog::Power],
            },
        ],
    )
    .unwrap();
    collector.start().unwrap();
    collector.poll().unwrap();
    let records = rows(&output);
    assert_eq!(
        bytes(
            &records,
            &game_source(&records, "session", "Power.log"),
            None
        ),
        b"game bytes"
    );
    assert_eq!(
        bytes(&records, &format!("recorder-42-{}", "a".repeat(32)), None),
        b"recorder bytes"
    );
    // A previously accepted session must still be checked; seen-name caching is
    // never permission to follow a later replacement symlink.
    let external = temp.path().join("outside");
    fs::create_dir(&external).unwrap();
    fs::write(external.join("Power.log"), b"outside").unwrap();
    fs::rename(&session, temp.path().join("original-session")).unwrap();
    symlink(external, &session).unwrap();
    assert!(
        collector
            .poll()
            .unwrap_err()
            .to_string()
            .contains("symlink")
    );
    collector.close("rejected link").unwrap();
    let after = rows(&output);
    assert_eq!(
        bytes(&after, &game_source(&after, "session", "Power.log"), None),
        b"game bytes"
    );
}
