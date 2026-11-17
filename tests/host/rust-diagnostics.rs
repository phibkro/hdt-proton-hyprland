use base64::{Engine, engine::general_purpose::STANDARD};
use hdt_host::{
    journal::{From, InputJournal, Source},
    timestamp::Timestamp,
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
        None,
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
    for record in &records {
        let wire = record["utc"].as_str().unwrap();
        assert_eq!(
            wire.len(),
            24,
            "journal timestamp must stay YYYY-MM-DDTHH:MM:SS.mmmZ"
        );
        assert!(wire.ends_with('Z'));
        Timestamp::parse_rfc3339(wire).unwrap();
    }
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
            None
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
            None
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
            None
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
        Some(root.clone()),
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
        Some(root.clone()),
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
        Some(root.clone()),
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
        Some(root),
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
        Some(root.clone()),
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
