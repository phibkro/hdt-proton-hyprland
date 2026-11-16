//! Export recorder sessions without conflating their independently started clocks.
use anyhow::{Result, bail, ensure};
use serde_json::{Map, Value, json};
use std::{
    collections::{HashMap, HashSet},
    ffi::OsString,
    fs::{self, OpenOptions},
    io::Write,
    os::unix::fs::OpenOptionsExt,
};

const MAX_SAFE: f64 = 9_007_199_254_740_991.0;
fn integer(value: &Value, minimum: f64) -> Result<f64> {
    let number = value
        .as_f64()
        .ok_or_else(|| anyhow::anyhow!("Unsupported or malformed flight-recorder event"))?;
    ensure!(
        number.is_finite() && number.fract() == 0.0 && number >= minimum && number <= MAX_SAFE,
        "Unsupported or malformed flight-recorder event"
    );
    Ok(number)
}

pub fn trace_events<'a>(lines: impl IntoIterator<Item = &'a str>) -> Result<Vec<Value>> {
    let mut sessions = HashMap::new();
    let mut seen = HashSet::new();
    let mut events = Vec::new();
    for line in lines {
        if line.trim().is_empty() {
            continue;
        }
        let event: Value = serde_json::from_str(line)?;
        ensure!(
            event["schema"].as_f64() == Some(1.0),
            "Unsupported or malformed flight-recorder event"
        );
        let session = event["session"]
            .as_str()
            .filter(|s| !s.is_empty())
            .ok_or_else(|| anyhow::anyhow!("Unsupported or malformed flight-recorder event"))?;
        let seq = integer(&event["seq"], 1.0)?;
        let time = integer(&event["tsUS"], 0.0)?;
        let thread = integer(&event["thread"], 1.0)?;
        let name = event["name"]
            .as_str()
            .ok_or_else(|| anyhow::anyhow!("Unsupported or malformed flight-recorder event"))?;
        let data = event["data"]
            .as_object()
            .ok_or_else(|| anyhow::anyhow!("Unsupported or malformed flight-recorder event"))?;
        ensure!(
            seen.insert((session.to_owned(), seq as u64)),
            "Duplicate event: pass either rolling files or the stall capture, not both"
        );
        let next = sessions.len() + 1;
        let pid = *sessions.entry(session.to_owned()).or_insert(next);
        let duration = if name == "ui.dispatch" {
            data.get("durationMS")
        } else {
            None
        };
        let mut output = Map::new();
        output.insert("name".into(), json!(name));
        output.insert(
            "cat".into(),
            json!(name.split('.').next().unwrap_or_default()),
        );
        output.insert("pid".into(), json!(pid));
        output.insert("tid".into(), json!(thread as u64));
        if let Some(duration) = duration {
            let duration = duration
                .as_f64()
                .ok_or_else(|| anyhow::anyhow!("Invalid dispatcher duration"))?;
            ensure!(
                duration.is_finite() && duration >= 0.0 && duration * 1000.0 <= time + 1000.0,
                "Invalid dispatcher duration"
            );
            output.insert("ph".into(), json!("X"));
            output.insert("dur".into(), json!(duration * 1000.0));
            output.insert("ts".into(), json!((time - duration * 1000.0).max(0.0)));
        } else {
            output.insert("ph".into(), json!("i"));
            output.insert("s".into(), json!("t"));
            output.insert("ts".into(), json!(time));
        }
        let mut args = data.clone();
        args.insert("session".into(), json!(session));
        args.insert("seq".into(), json!(seq as u64));
        output.insert("args".into(), Value::Object(args));
        events.push(Value::Object(output));
    }
    events.sort_by(|a, b| {
        a["pid"].as_u64().cmp(&b["pid"].as_u64()).then_with(|| {
            a["ts"]
                .as_f64()
                .unwrap_or_default()
                .total_cmp(&b["ts"].as_f64().unwrap_or_default())
        })
    });
    Ok(events)
}

pub fn run(args: &[OsString]) -> Result<()> {
    if args.len() < 2 {
        bail!("Usage: hdt-proton trace-export OUTPUT.json INPUT.jsonl [INPUT.jsonl ...]");
    }
    let inputs: Vec<String> = args[1..]
        .iter()
        .map(fs::read_to_string)
        .collect::<std::io::Result<_>>()?;
    let events = trace_events(inputs.iter().flat_map(|text| text.split('\n')))?;
    ensure!(!events.is_empty(), "No trace events found");
    let mut output = OpenOptions::new()
        .write(true)
        .create_new(true)
        .mode(0o600)
        .open(&args[0])?;
    serde_json::to_writer(&mut output, &json!({"traceEvents": events}))?;
    output.flush()?;
    println!(
        "Exported {} events to {}",
        events.len(),
        args[0].to_string_lossy()
    );
    Ok(())
}
