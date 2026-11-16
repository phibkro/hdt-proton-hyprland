//! Native client-owned state. Revision checks and one lock make lost updates explicit.
use crate::{
    deckcode::{self, DeckContent},
    snapshot,
};
use anyhow::{Context, Result, ensure};
use chrono::Utc;
use serde::{Deserialize, Serialize};
use std::{
    ffi::OsString,
    fs::{self, OpenOptions},
    io::{Read, Write},
    os::unix::fs::{DirBuilderExt, OpenOptionsExt},
    path::Path,
};
const STORE_BYTES: usize = 2 * 1024 * 1024;
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct DeckDraft {
    pub id: Option<String>,
    pub name: String,
    pub hero_class: String,
    pub content: DeckContent,
    pub notes: String,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct DeckEntry {
    pub id: String,
    pub name: String,
    pub hero_class: String,
    pub content: DeckContent,
    pub notes: String,
    pub archived: bool,
    pub created_at: String,
    pub updated_at: String,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct MatchRecord {
    pub id: String,
    pub deck_id: Option<String>,
    pub observed_at: String,
    pub mode: String,
    pub result: String,
    pub player_hero: String,
    pub opponent_hero: String,
    pub turns: u32,
    pub placement: Option<u8>,
    pub notes: String,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ClientState {
    pub schema_version: u8,
    pub revision: u64,
    pub selected_deck_id: Option<String>,
    pub decks: Vec<DeckEntry>,
    pub matches: Vec<MatchRecord>,
}
impl Default for ClientState {
    fn default() -> Self {
        Self {
            schema_version: 1,
            revision: 0,
            selected_deck_id: None,
            decks: vec![],
            matches: vec![],
        }
    }
}
impl ClientState {
    fn validate(&self) -> Result<()> {
        ensure!(
            self.schema_version == 1 && self.decks.len() <= 500 && self.matches.len() <= 5000,
            "unsupported or oversized client state"
        );
        let mut ids = std::collections::HashSet::new();
        for deck in &self.decks {
            uuid::Uuid::parse_str(&deck.id).context("invalid deck ID")?;
            chrono::DateTime::parse_from_rfc3339(&deck.created_at)
                .context("invalid deck creation time")?;
            chrono::DateTime::parse_from_rfc3339(&deck.updated_at)
                .context("invalid deck update time")?;
            ensure!(ids.insert(&deck.id), "duplicate deck ID");
            validate_draft(&DeckDraft {
                id: Some(deck.id.clone()),
                name: deck.name.clone(),
                hero_class: deck.hero_class.clone(),
                content: deck.content.clone(),
                notes: deck.notes.clone(),
            })?;
        }
        ensure!(
            self.selected_deck_id.as_ref().is_none_or(|id| self
                .decks
                .iter()
                .any(|deck| &deck.id == id && !deck.archived)),
            "selected deck does not exist or is archived"
        );
        let mut matches = std::collections::HashSet::new();
        for record in &self.matches {
            validate_match(record)?;
            ensure!(
                record.deck_id.as_ref().is_none_or(|id| ids.contains(id)),
                "match references unknown deck"
            );
            ensure!(matches.insert(&record.id), "duplicate match ID");
        }
        Ok(())
    }
}
fn validate_draft(deck: &DeckDraft) -> Result<()> {
    ensure!(
        !deck.name.trim().is_empty()
            && deck.name.len() <= 256
            && deck.hero_class.len() <= 64
            && deck.notes.len() <= 8192,
        "invalid deck name/class/notes"
    );
    deck.content.validate()
}
fn validate_match(record: &MatchRecord) -> Result<()> {
    uuid::Uuid::parse_str(&record.id).context("invalid match ID")?;
    chrono::DateTime::parse_from_rfc3339(&record.observed_at).context("invalid match time")?;
    ensure!(
        ["Win", "Loss", "Draw", "Unknown"].contains(&record.result.as_str()),
        "unknown match result"
    );
    ensure!(
        !record.mode.is_empty()
            && record.mode.len() <= 128
            && record.player_hero.len() <= 128
            && record.opponent_hero.len() <= 128
            && record.turns <= 1000
            && record.notes.len() <= 8192,
        "match exceeds bound"
    );
    ensure!(
        record
            .placement
            .is_none_or(|placement| (1..=8).contains(&placement)),
        "invalid placement"
    );
    Ok(())
}
#[derive(Debug, Deserialize)]
#[serde(
    tag = "operation",
    rename_all = "camelCase",
    rename_all_fields = "camelCase",
    deny_unknown_fields
)]
pub enum Request {
    List,
    UpsertDeck {
        expected_revision: u64,
        deck: DeckDraft,
    },
    ImportDeck {
        expected_revision: u64,
        name: Option<String>,
        code: String,
    },
    SelectDeck {
        expected_revision: u64,
        id: Option<String>,
    },
    ArchiveDeck {
        expected_revision: u64,
        id: String,
    },
    RecordMatch {
        expected_revision: u64,
        record: MatchRecord,
    },
    ExportDeck {
        id: String,
    },
}
fn load(path: &Path) -> Result<ClientState> {
    let state = match snapshot::read(path, STORE_BYTES)? {
        Some(bytes) => serde_json::from_slice(&bytes).context("invalid client state")?,
        None => ClientState::default(),
    };
    state.validate()?;
    Ok(state)
}
fn upsert(state: &mut ClientState, deck: DeckDraft) -> Result<()> {
    validate_draft(&deck)?;
    let now = Utc::now().to_rfc3339();
    if let Some(id) = deck.id {
        let existing = state
            .decks
            .iter_mut()
            .find(|entry| entry.id == id)
            .context("deck does not exist")?;
        existing.name = deck.name;
        existing.hero_class = deck.hero_class;
        existing.content = deck.content;
        existing.notes = deck.notes;
        existing.updated_at = now;
    } else {
        let id = uuid::Uuid::new_v4().to_string();
        state.decks.push(DeckEntry {
            id: id.clone(),
            name: deck.name,
            hero_class: deck.hero_class,
            content: deck.content,
            notes: deck.notes,
            archived: false,
            created_at: now.clone(),
            updated_at: now,
        });
        state.selected_deck_id = Some(id);
    }
    Ok(())
}
pub fn execute(path: &Path, request: Request) -> Result<serde_json::Value> {
    match request {
        Request::List => return Ok(serde_json::to_value(load(path)?)?),
        Request::ExportDeck { id } => {
            let state = load(path)?;
            let deck = state
                .decks
                .iter()
                .find(|deck| deck.id == id)
                .context("deck does not exist")?;
            return Ok(
                serde_json::json!({"code": deckcode::encode(&deck.content)?, "name": deck.name}),
            );
        }
        _ => {}
    }
    let parent = path
        .parent()
        .filter(|parent| !parent.as_os_str().is_empty())
        .context("client store requires a parent directory")?;
    fs::DirBuilder::new()
        .recursive(true)
        .mode(0o700)
        .create(parent)?;
    let lock_path = path.with_extension("lock");
    let lock = OpenOptions::new()
        .read(true)
        .write(true)
        .create(true)
        .truncate(false)
        .mode(0o600)
        .custom_flags(nix::libc::O_NOFOLLOW | nix::libc::O_NONBLOCK)
        .open(lock_path)?;
    ensure!(
        lock.metadata()?.is_file(),
        "client lock is not a regular file"
    );
    lock.lock().context("locking client state")?;
    let mut state = load(path)?;
    let expected = match &request {
        Request::UpsertDeck {
            expected_revision, ..
        }
        | Request::ImportDeck {
            expected_revision, ..
        }
        | Request::SelectDeck {
            expected_revision, ..
        }
        | Request::ArchiveDeck {
            expected_revision, ..
        }
        | Request::RecordMatch {
            expected_revision, ..
        } => *expected_revision,
        _ => unreachable!(),
    };
    ensure!(
        expected == state.revision,
        "client state changed; reload before editing (expected {expected}, actual {})",
        state.revision
    );
    match request {
        Request::UpsertDeck { deck, .. } => upsert(&mut state, deck)?,
        Request::ImportDeck { name, code, .. } => upsert(
            &mut state,
            DeckDraft {
                id: None,
                name: name
                    .filter(|name| !name.trim().is_empty())
                    .unwrap_or_else(|| {
                        code.lines()
                            .find_map(|line| {
                                line.trim()
                                    .strip_prefix("###")
                                    .map(|name| name.trim().to_owned())
                            })
                            .filter(|name| !name.is_empty())
                            .unwrap_or_else(|| "Imported deck".to_owned())
                    }),
                hero_class: String::new(),
                content: deckcode::decode(&code)?,
                notes: String::new(),
            },
        )?,
        Request::SelectDeck { id, .. } => state.selected_deck_id = id,
        Request::ArchiveDeck { id, .. } => {
            state
                .decks
                .iter_mut()
                .find(|deck| deck.id == id)
                .context("deck does not exist")?
                .archived = true;
            if state.selected_deck_id.as_ref() == Some(&id) {
                state.selected_deck_id = None;
            }
        }
        Request::RecordMatch { record, .. } => {
            validate_match(&record)?;
            ensure!(
                record
                    .deck_id
                    .as_ref()
                    .is_none_or(|id| state.decks.iter().any(|deck| &deck.id == id)),
                "match references unknown deck"
            );
            if let Some(existing) = state.matches.iter_mut().find(|entry| entry.id == record.id) {
                *existing = record;
            } else {
                state.matches.push(record);
            }
        }
        _ => unreachable!(),
    }
    state.revision = state
        .revision
        .checked_add(1)
        .context("client revision overflow")?;
    state.validate()?;
    let bytes = serde_json::to_vec_pretty(&state)?;
    ensure!(
        bytes.len() <= STORE_BYTES,
        "client store exceeds 2 MiB preview bound; no changes were saved"
    );
    let mut temporary = tempfile::NamedTempFile::new_in(parent)?;
    temporary.write_all(&bytes)?;
    temporary.as_file().sync_all()?;
    temporary
        .persist(path)
        .context("atomically replacing client state")?;
    fs::File::open(parent)?.sync_all()?;
    Ok(serde_json::to_value(state)?)
}
pub fn run(args: &[OsString]) -> Result<()> {
    let [path] = args else {
        anyhow::bail!("Usage: hdt-host client STORE_PATH (JSON request on stdin)");
    };
    let mut bytes = Vec::new();
    std::io::stdin()
        .lock()
        .take(65537)
        .read_to_end(&mut bytes)?;
    ensure!(bytes.len() <= 65536, "client request exceeds 64 KiB");
    let result = execute(
        Path::new(path),
        serde_json::from_slice(&bytes).context("invalid client request")?,
    )?;
    println!("{}", serde_json::to_string(&result)?);
    Ok(())
}
