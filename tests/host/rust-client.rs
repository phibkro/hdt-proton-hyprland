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

#[test]
fn existing_library_and_dotnet_match_times_survive_the_timestamp_migration() {
    let directory = tempfile::tempdir().unwrap();
    let path = directory.path().join("library.json");
    let baseline = ok(
        &path,
        json!({"operation":"upsertDeck","expectedRevision":0,"deck":{"id":null,"name":"Time compatibility","heroClass":"Mage","content":{"heroDbfId":637,"format":2,"cards":[{"dbfId":77,"count":1}],"sideboards":[]},"notes":"Preserve this note"}}),
    );
    let deck_id = baseline["decks"][0]["id"].clone();
    // These are existing wire formats, including .NET's seven fractional digits.
    for timestamp in [
        "2026-01-01T00:00:00Z",
        "2026-01-01T00:00:00.123+00:00",
        "2026-01-01T01:00:00.1234567+01:00",
        "1969-12-31T23:59:59.999999999Z",
    ] {
        let mut stored = baseline.clone();
        stored["decks"][0]["createdAt"] = json!(timestamp);
        stored["decks"][0]["updatedAt"] = json!(timestamp);
        let bytes = serde_json::to_vec(&stored).unwrap();
        fs::write(&path, &bytes).unwrap();
        assert_eq!(ok(&path, json!({"operation":"list"})), stored);
        assert_eq!(
            fs::read(&path).unwrap(),
            bytes,
            "browsing must not rewrite old timestamps"
        );
        let record = json!({"id":"6d67ed8a-1b65-48e6-b047-bbd79dd2161d","deckId":deck_id,"observedAt":timestamp,"mode":"Arena","result":"Win","playerHero":"Mage","opponentHero":"Warrior","turns":8,"placement":null,"notes":""});
        let updated = ok(
            &path,
            json!({"operation":"recordMatch","expectedRevision":1,"record":record}),
        );
        assert_eq!(updated["matches"][0]["observedAt"], timestamp);
        assert_eq!(updated["decks"], stored["decks"]);
    }
    for invalid in [
        "2026-02-30T00:00:00Z",
        "2026-01-01T00:00:00",
        "2026-01-01T00:00:00+25:00",
        "2026-01-01T00:00:00Z[UTC]",
    ] {
        let bytes = serde_json::to_vec(&baseline).unwrap();
        fs::write(&path, &bytes).unwrap();
        let record = json!({"id":"6d67ed8a-1b65-48e6-b047-bbd79dd2161d","deckId":deck_id,"observedAt":invalid,"mode":"Arena","result":"Win","playerHero":"Mage","opponentHero":"Warrior","turns":8,"placement":null,"notes":""});
        let rejected = request(
            &path,
            json!({"operation":"recordMatch","expectedRevision":1,"record":record}),
        );
        assert!(
            !rejected.status.success(),
            "invalid match time accepted: {invalid}"
        );
        assert_eq!(
            fs::read(&path).unwrap(),
            bytes,
            "invalid timestamp must not mutate history"
        );
    }
}

#[test]
fn metadata_defaults_explicit_updates_and_old_upserts_preserve_library_identity() {
    let directory = tempfile::tempdir().unwrap();
    let path = directory.path().join("library.json");
    let content =
        json!({"heroDbfId":637,"format":2,"cards":[{"dbfId":77,"count":1}],"sideboards":[]});
    let baseline = ok(
        &path,
        json!({"operation":"upsertDeck","expectedRevision":0,"deck":{"id":null,"name":"Metadata fixture","heroClass":"Mage","content":content,"notes":"Original note"}}),
    );
    let id = baseline["decks"][0]["id"].clone();
    let empty = json!({"tags":[],"website":null});
    assert_eq!(baseline["decks"][0]["metadata"], empty);
    // Existing schema1 records have no metadata object. Reading supplies defaults without rewriting.
    let mut old = baseline.clone();
    old["decks"][0].as_object_mut().unwrap().remove("metadata");
    let old_bytes = serde_json::to_vec(&old).unwrap();
    fs::write(&path, &old_bytes).unwrap();
    assert_eq!(ok(&path, json!({"operation":"list"})), baseline);
    assert_eq!(fs::read(&path).unwrap(), old_bytes);
    let tags = json!(["Arena", "arena", "日本語", "😀".repeat(64)]);
    let address = "https://例え.テスト/CaSe?a=1#notes";
    let changed = ok(
        &path,
        json!({"operation":"setDeckMetadata","expectedRevision":1,"id":id,"metadata":{"tags":tags,"website":{"action":"set","value":address}}}),
    );
    let metadata = json!({"tags":tags,"website":address});
    assert_eq!(changed["decks"][0]["metadata"], metadata);
    assert_eq!(changed["selectedDeckId"], id);
    for field in [
        "id",
        "name",
        "heroClass",
        "content",
        "notes",
        "archived",
        "createdAt",
    ] {
        assert_eq!(changed["decks"][0][field], baseline["decks"][0][field]);
    }
    let changed_bytes = fs::read(&path).unwrap();
    let mut invalids = vec![
        json!({"tags":[],"website":null}),
        json!({"tags":[]}),
        json!({"website":{"action":"clear"}}),
        json!({"tags":[""],"website":{"action":"clear"}}),
        json!({"tags":[" A"],"website":{"action":"clear"}}),
        json!({"tags":["A","A"],"website":{"action":"clear"}}),
        json!({"tags":["A\tB"],"website":{"action":"clear"}}),
        json!({"tags":["😀".repeat(65)],"website":{"action":"clear"}}),
        json!({"tags":(0..17).map(|n| format!("Tag{n}")).collect::<Vec<_>>(),"website":{"action":"clear"}}),
        json!({"tags":[],"website":{"action":"clear","value":"https://example.test"}}),
    ];
    for website in [
        "",
        "relative/path",
        "javascript:alert(1)",
        "ftp://example.test/",
        "https://",
        "https://user@example.test/",
        "https://example.test/with space",
        "https://example.test/\nline",
    ] {
        invalids.push(json!({"tags":[],"website":{"action":"set","value":website}}));
    }
    invalids.push(json!({"tags":[],"website":{"action":"set","value":format!("https://example.test/{}", "x".repeat(2048))}}));
    for metadata in invalids {
        let rejected = request(
            &path,
            json!({"operation":"setDeckMetadata","expectedRevision":2,"id":id,"metadata":metadata}),
        );
        assert!(
            !rejected.status.success(),
            "unexpected accepted metadata: {metadata}"
        );
        assert_eq!(
            fs::read(&path).unwrap(),
            changed_bytes,
            "invalid metadata changed persisted bytes"
        );
    }
    let unknown = request(
        &path,
        json!({"operation":"setDeckMetadata","expectedRevision":2,"id":"f7d368ae-b616-4baa-9593-c78092747bf2","metadata":{"tags":[],"website":{"action":"clear"}}}),
    );
    assert!(!unknown.status.success());
    assert_eq!(fs::read(&path).unwrap(), changed_bytes);
    let stale = request(
        &path,
        json!({"operation":"setDeckMetadata","expectedRevision":1,"id":id,"metadata":{"tags":[],"website":{"action":"clear"}}}),
    );
    assert!(!stale.status.success());
    assert_eq!(fs::read(&path).unwrap(), changed_bytes);
    // Existing callers cannot overwrite metadata by omission: ordinary save has no metadata field.
    let saved = ok(
        &path,
        json!({"operation":"upsertDeck","expectedRevision":2,"deck":{"id":id,"name":"Renamed","heroClass":"Mage","content":content,"notes":"New note"}}),
    );
    assert_eq!(saved["decks"][0]["metadata"], metadata);
    assert_eq!(saved["selectedDeckId"], id);
    let record = json!({"id":"6d67ed8a-1b65-48e6-b047-bbd79dd2161d","deckId":id,"observedAt":"2026-01-01T00:00:00Z","mode":"Arena","result":"Win","playerHero":"Mage","opponentHero":"Warrior","turns":8,"placement":null,"notes":""});
    let recorded = ok(
        &path,
        json!({"operation":"recordMatch","expectedRevision":3,"record":record}),
    );
    let archived = ok(
        &path,
        json!({"operation":"setArchived","expectedRevision":4,"id":id,"archived":true}),
    );
    assert_eq!(archived["decks"][0]["metadata"], metadata);
    let archived_bytes = fs::read(&path).unwrap();
    assert!(!request(&path, json!({"operation":"setDeckMetadata","expectedRevision":5,"id":id,"metadata":{"tags":[],"website":{"action":"clear"}}})).status.success());
    assert_eq!(fs::read(&path).unwrap(), archived_bytes);
    let restored = ok(
        &path,
        json!({"operation":"setArchived","expectedRevision":5,"id":id,"archived":false}),
    );
    assert_eq!(restored["decks"][0]["metadata"], metadata);
    assert_eq!(restored["matches"], recorded["matches"]);
    let cleared = ok(
        &path,
        json!({"operation":"setDeckMetadata","expectedRevision":6,"id":id,"metadata":{"tags":[],"website":{"action":"clear"}}}),
    );
    assert_eq!(cleared["decks"][0]["metadata"], empty);
    assert_eq!(cleared["decks"][0]["notes"], "New note");
    assert_eq!(cleared["matches"], recorded["matches"]);
    assert_eq!(cleared["selectedDeckId"], restored["selectedDeckId"]);
    assert_eq!(cleared["decks"][0]["id"], id);
    assert_eq!(cleared["decks"][0]["content"], content);
    let mut corrupted = cleared;
    corrupted["decks"][0]["metadata"] = json!({"tags":[" A"],"website":null});
    let corrupted_bytes = serde_json::to_vec(&corrupted).unwrap();
    fs::write(&path, &corrupted_bytes).unwrap();
    assert!(!request(&path, json!({"operation":"list"})).status.success());
    assert_eq!(
        fs::read(&path).unwrap(),
        corrupted_bytes,
        "invalid stored metadata must fail, never repair or truncate silently"
    );
}

fn duplicate_source_fixture(path: &std::path::Path) -> Value {
    let content = json!({"heroDbfId":637,"format":2,"cards":[{"dbfId":77,"count":1},{"dbfId":100,"count":2}],"sideboards":[{"ownerDbfId":77,"dbfId":200,"count":2}]});
    let created = ok(
        path,
        json!({"operation":"upsertDeck","expectedRevision":0,"deck":{"id":null,"name":"Source deck","heroClass":"Mage","content":content,"notes":"Saved source notes"}}),
    );
    let source_id = created["decks"][0]["id"].clone();
    ok(
        path,
        json!({"operation":"setDeckMetadata","expectedRevision":1,"id":source_id,"metadata":{"tags":["Arena","arena","日本語"],"website":{"action":"set","value":"https://例え.テスト/CaSe?a=1#notes"}}}),
    );
    ok(
        path,
        json!({"operation":"upsertDeck","expectedRevision":2,"deck":{"id":null,"name":"Other deck","heroClass":"Mage","content":content,"notes":"Other saved notes"}}),
    );
    for (revision, id, deck_id) in [
        (3, "9c685e68-1fa0-43a3-aedb-b3d913a02b14", source_id),
        (4, "715a21bb-0917-4d37-97b8-859f5b20b082", Value::Null),
    ] {
        ok(
            path,
            json!({"operation":"recordMatch","expectedRevision":revision,"record":{"id":id,"deckId":deck_id,"observedAt":"2026-01-01T00:00:00Z","mode":"Owned duplicate fixture","result":"Win","playerHero":"Mage","opponentHero":"Warrior","turns":8,"placement":null,"notes":"Preserve history"}}),
        );
    }
    let mut stored = ok(path, json!({"operation":"list"}));
    for deck in stored["decks"].as_array_mut().unwrap() {
        deck["createdAt"] = json!("2000-01-01T00:00:00Z");
        deck["updatedAt"] = json!("2000-01-01T00:00:00Z");
    }
    fs::write(path, serde_json::to_vec_pretty(&stored).unwrap()).unwrap();
    stored
}

#[test]
fn saved_duplicate_copies_content_and_metadata_without_history_or_tracking_changes() {
    let directory = tempfile::tempdir().unwrap();
    let path = directory.path().join("library.json");
    let baseline = duplicate_source_fixture(&path);
    for (archived, no_selection) in [(false, false), (true, false), (false, true)] {
        let mut before = baseline.clone();
        before["decks"][0]["archived"] = json!(archived);
        if no_selection {
            before["selectedDeckId"] = Value::Null;
        }
        fs::write(&path, serde_json::to_vec_pretty(&before).unwrap()).unwrap();
        let source = &before["decks"][0];
        let duplicated = ok(
            &path,
            json!({"operation":"duplicateDeck","expectedRevision":5,"sourceId":source["id"],"name":"Source deck"}),
        );
        let decks = duplicated["decks"].as_array().unwrap();
        assert_eq!(decks.len(), 3);
        assert_eq!(&decks[..2], before["decks"].as_array().unwrap().as_slice());
        assert_eq!(duplicated["revision"], 6);
        assert_eq!(duplicated["selectedDeckId"], before["selectedDeckId"]);
        assert_eq!(duplicated["matches"], before["matches"]);
        let duplicate = &decks[2];
        uuid::Uuid::parse_str(duplicate["id"].as_str().unwrap()).unwrap();
        assert!(
            before["decks"]
                .as_array()
                .unwrap()
                .iter()
                .all(|deck| deck["id"] != duplicate["id"])
        );
        for field in ["name", "heroClass", "content", "notes", "metadata"] {
            assert_eq!(
                duplicate[field], source[field],
                "saved {field} must be copied"
            );
        }
        assert_eq!(duplicate["archived"], false);
        assert_eq!(duplicate["createdAt"], duplicate["updatedAt"]);
        assert_ne!(duplicate["createdAt"], source["createdAt"]);
        assert_eq!(ok(&path, json!({"operation":"list"})), duplicated);
        let edited = ok(
            &path,
            json!({"operation":"upsertDeck","expectedRevision":6,"deck":{"id":duplicate["id"],"name":"Independent edit","heroClass":"Mage","content":duplicate["content"],"notes":"Only the duplicate changed"}}),
        );
        assert_eq!(
            &edited["decks"].as_array().unwrap()[..2],
            before["decks"].as_array().unwrap().as_slice()
        );
        assert_eq!(edited["decks"][2]["metadata"], duplicate["metadata"]);
        assert_eq!(edited["selectedDeckId"], before["selectedDeckId"]);
        assert_eq!(edited["matches"], before["matches"]);
    }
    let mut legacy = baseline;
    legacy["decks"][0]
        .as_object_mut()
        .unwrap()
        .remove("metadata");
    let old_bytes = serde_json::to_vec_pretty(&legacy).unwrap();
    fs::write(&path, &old_bytes).unwrap();
    let normalized = ok(&path, json!({"operation":"list"}));
    assert_eq!(fs::read(&path).unwrap(), old_bytes);
    let duplicated = ok(
        &path,
        json!({"operation":"duplicateDeck","expectedRevision":5,"sourceId":normalized["decks"][0]["id"],"name":"Explicit duplicate name"}),
    );
    assert_eq!(duplicated["decks"][2]["name"], "Explicit duplicate name");
    assert_eq!(
        duplicated["decks"][2]["metadata"],
        json!({"tags":[],"website":null})
    );
    assert_eq!(
        &duplicated["decks"].as_array().unwrap()[..2],
        normalized["decks"].as_array().unwrap().as_slice()
    );
    assert_eq!(duplicated["selectedDeckId"], normalized["selectedDeckId"]);
    assert_eq!(duplicated["matches"], normalized["matches"]);
    assert_eq!(ok(&path, json!({"operation":"list"})), duplicated);
}

#[test]
fn duplicate_rejections_and_capacity_limits_leave_exact_saved_bytes_unchanged() {
    let directory = tempfile::tempdir().unwrap();
    let path = directory.path().join("library.json");
    let baseline = duplicate_source_fixture(&path);
    let source_id = baseline["decks"][0]["id"].clone();
    let before = fs::read(&path).unwrap();
    for body in [
        json!({"operation":"duplicateDeck","expectedRevision":5,"sourceId":"f7d368ae-b616-4baa-9593-c78092747bf2","name":"Unknown source"}),
        json!({"operation":"duplicateDeck","expectedRevision":4,"sourceId":source_id,"name":"Stale"}),
        json!({"operation":"duplicateDeck","expectedRevision":5,"sourceId":source_id,"name":"","copyHistory":true}),
        json!({"operation":"duplicateDeck","expectedRevision":5,"sourceId":source_id,"name":"Valid name","copyHistory":false}),
        json!({"operation":"duplicateDeck","expectedRevision":5,"sourceId":source_id}),
        json!({"operation":"duplicateDeck","expectedRevision":5,"name":"Missing source"}),
        json!({"operation":"duplicateDeck","sourceId":source_id,"name":"Missing revision"}),
        json!({"operation":"duplicateDeck","expectedRevision":5,"sourceId":source_id,"name":""}),
        json!({"operation":"duplicateDeck","expectedRevision":5,"sourceId":source_id,"name":"   "}),
        json!({"operation":"duplicateDeck","expectedRevision":5,"sourceId":source_id,"name":"n".repeat(257)}),
    ] {
        let rejected = request(&path, body);
        assert!(
            !rejected.status.success(),
            "invalid duplicate request accepted"
        );
        assert_eq!(fs::read(&path).unwrap(), before);
    }
    let mut capacity = baseline.clone();
    while capacity["decks"].as_array().unwrap().len() < 500 {
        let mut filler = baseline["decks"][0].clone();
        filler["id"] = json!(
            uuid::Uuid::from_u128(capacity["decks"].as_array().unwrap().len() as u128 + 1)
                .to_string()
        );
        capacity["decks"].as_array_mut().unwrap().push(filler);
    }
    let capacity_bytes = serde_json::to_vec_pretty(&capacity).unwrap();
    assert!(capacity_bytes.len() < 2 * 1024 * 1024);
    fs::write(&path, &capacity_bytes).unwrap();
    assert_eq!(
        ok(&path, json!({"operation":"list"}))["decks"]
            .as_array()
            .unwrap()
            .len(),
        500
    );
    let rejected = request(
        &path,
        json!({"operation":"duplicateDeck","expectedRevision":5,"sourceId":source_id,"name":"Over capacity"}),
    );
    assert!(!rejected.status.success());
    assert_eq!(fs::read(&path).unwrap(), capacity_bytes);

    let limit = 2 * 1024 * 1024;
    let mut byte_capacity = baseline;
    byte_capacity["decks"][0]["notes"] = json!("n".repeat(8192));
    loop {
        let mut filler = byte_capacity["decks"][0].clone();
        filler["id"] = json!(
            uuid::Uuid::from_u128(byte_capacity["decks"].as_array().unwrap().len() as u128 + 1)
                .to_string()
        );
        byte_capacity["decks"].as_array_mut().unwrap().push(filler);
        if serde_json::to_vec_pretty(&byte_capacity).unwrap().len() > limit {
            byte_capacity["decks"].as_array_mut().unwrap().pop();
            break;
        }
    }
    assert!(byte_capacity["decks"].as_array().unwrap().len() < 500);
    let full_bytes = serde_json::to_vec_pretty(&byte_capacity).unwrap();
    fs::write(&path, &full_bytes).unwrap();
    assert_eq!(ok(&path, json!({"operation":"list"})), byte_capacity);
    let rejected = request(
        &path,
        json!({"operation":"duplicateDeck","expectedRevision":5,"sourceId":source_id,"name":"Source deck"}),
    );
    assert!(!rejected.status.success());
    assert!(String::from_utf8_lossy(&rejected.stderr).contains("client store exceeds 2 MiB"));
    assert_eq!(fs::read(&path).unwrap(), full_bytes);
}
