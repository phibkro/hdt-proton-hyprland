//! Native library workflows through the real CLI and on-disk transaction boundary.
use serde_json::{Value, json};
use std::{
    fs,
    io::Write,
    process::{Command, Stdio},
};
fn request(path: &std::path::Path, body: Value) -> std::process::Output {
    let mut child = Command::new(env!("CARGO_BIN_EXE_hdt-host"))
        .arg("client")
        .arg(path)
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .unwrap();
    child
        .stdin
        .take()
        .unwrap()
        .write_all(body.to_string().as_bytes())
        .unwrap();
    child.wait_with_output().unwrap()
}
fn ok(path: &std::path::Path, body: Value) -> Value {
    let result = request(path, body);
    assert!(
        result.status.success(),
        "{}",
        String::from_utf8_lossy(&result.stderr)
    );
    serde_json::from_slice(&result.stdout).unwrap()
}
#[test]
fn deck_edit_import_export_archive_and_history_are_atomic_and_revision_checked() {
    let directory = tempfile::tempdir().unwrap();
    let path = directory.path().join("client/library.json");
    let initial = ok(&path, json!({"operation":"list"}));
    assert_eq!(initial["revision"], 0);
    assert!(!path.exists());
    let content = json!({"heroDbfId":637,"format":2,"cards":[{"dbfId":77,"count":1},{"dbfId":100,"count":2}],"sideboards":[]});
    let state = ok(
        &path,
        json!({"operation":"upsertDeck","expectedRevision":0,"deck":{"id":null,"name":"Fixture Mage","heroClass":"Mage","content":content,"notes":"Original note"}}),
    );
    let id = state["decks"][0]["id"].as_str().unwrap();
    assert_eq!(state["selectedDeckId"], id);
    let export = ok(&path, json!({"operation":"exportDeck","id":id}));
    // Golden bytes follow the pinned HearthDb version-1 format; not a roundtrip-only assertion.
    assert_eq!(export["code"], "AAECAf0EAU0BZAAA");
    let original_bytes = fs::read(&path).unwrap();
    let stale = request(
        &path,
        json!({"operation":"selectDeck","expectedRevision":0,"id":id}),
    );
    assert!(!stale.status.success());
    assert_eq!(fs::read(&path).unwrap(), original_bytes);
    let imported = ok(
        &path,
        json!({"operation":"importDeck","expectedRevision":1,"name":"Imported fixture","code":"### Fixture\nAAECAf0EAU0BZAAA"}),
    );
    assert_eq!(imported["decks"][1]["content"], content);
    let record = json!({"id":"6d67ed8a-1b65-48e6-b047-bbd79dd2161d","deckId":id,"observedAt":"2026-01-01T00:00:00Z","mode":"Manual fixture","result":"Win","playerHero":"Mage","opponentHero":"Warrior","turns":8,"placement":null,"notes":"Owned acceptance fixture"});
    let history = ok(
        &path,
        json!({"operation":"recordMatch","expectedRevision":2,"record":record}),
    );
    assert_eq!(history["matches"].as_array().unwrap().len(), 1);
    let updated = ok(
        &path,
        json!({"operation":"recordMatch","expectedRevision":3,"record":record}),
    );
    assert_eq!(
        updated["matches"].as_array().unwrap().len(),
        1,
        "same source identity updates rather than duplicates"
    );
    let archived = ok(
        &path,
        json!({"operation":"setArchived","expectedRevision":4,"id":id,"archived":true}),
    );
    assert_eq!(archived["decks"][0]["archived"], true);
    assert_eq!(archived["decks"][0]["notes"], "Original note");
    let restored = ok(
        &path,
        json!({"operation":"setArchived","expectedRevision":5,"id":id,"archived":false}),
    );
    assert_eq!(restored["decks"][0]["archived"], false);
    assert_eq!(restored["decks"][0]["content"], content);
    assert_eq!(restored["decks"][0]["notes"], "Original note");
    assert_eq!(
        restored["selectedDeckId"], imported["selectedDeckId"],
        "restoring must not silently change tracking selection"
    );
    assert_eq!(
        restored["matches"], updated["matches"],
        "archive/restore must preserve historical association"
    );
    use std::os::unix::fs::PermissionsExt;
    assert_eq!(
        fs::metadata(path).unwrap().permissions().mode() & 0o777,
        0o600
    );
}
#[test]
fn sideboards_roundtrip_and_invalid_inputs_cannot_mutate_state() {
    let directory = tempfile::tempdir().unwrap();
    let path = directory.path().join("library.json");
    let content = hdt_host::deckcode::DeckContent {
        hero_dbf_id: 637,
        format: 2,
        cards: vec![hdt_host::deckcode::CardCount {
            dbf_id: 77,
            count: 1,
        }],
        sideboards: vec![hdt_host::deckcode::SideboardCard {
            owner_dbf_id: 77,
            dbf_id: 100,
            count: 2,
        }],
    };
    let code = hdt_host::deckcode::encode(&content).unwrap();
    assert_eq!(hdt_host::deckcode::decode(&code).unwrap(), content);
    for invalid in [
        "",
        "garbage",
        "AAECAf0EAU0BZAAA\nAAECAf0EAU0BZAAA",
        "AAECAf0E",
    ] {
        assert!(hdt_host::deckcode::decode(invalid).is_err());
        let result = request(
            &path,
            json!({"operation":"importDeck","expectedRevision":0,"name":"Bad","code":invalid}),
        );
        assert!(!result.status.success());
        assert!(!path.exists());
    }
}
