//! Original projection of an explicitly supplied localized HearthstoneJSON file.
//! No catalog or artwork is bundled or downloaded. Format reference:
//! https://hearthstonejson.com/docs/cards.html (dbfId is the deckstring identity).
use anyhow::{Context, Result, ensure};
use serde::{Deserialize, Serialize};
use std::{
    collections::HashSet,
    ffi::OsString,
    io::{Read, Write},
    path::Path,
};
#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawCard {
    dbf_id: u32,
    id: String,
    name: String,
    #[serde(rename = "type")]
    card_type: Option<String>,
    card_class: Option<String>,
    #[serde(default)]
    classes: Vec<String>,
    cost: Option<u32>,
    #[serde(default)]
    collectible: bool,
}
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Card {
    dbf_id: u32,
    id: String,
    name: String,
    card_type: Option<String>,
    card_class: Option<String>,
    classes: Vec<String>,
    cost: Option<u32>,
    collectible: bool,
}
#[derive(Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Search {
    query: String,
    include_non_collectible: bool,
}
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Response {
    schema_version: u8,
    catalog_cards: usize,
    matches: usize,
    cards: Vec<Card>,
}
fn bounded(text: &str, maximum: usize) -> bool {
    !text.trim().is_empty() && text.len() <= maximum && !text.chars().any(char::is_control)
}
pub fn search(path: &Path, request: Search) -> Result<Response> {
    ensure!(
        request.query.len() <= 256 && !request.query.chars().any(char::is_control),
        "catalog query must be at most 256 bytes without controls"
    );
    let bytes = crate::snapshot::read(path, crate::snapshot::MAX_BYTES)?
        .context("catalog file does not exist")?;
    let raw: Vec<RawCard> = serde_json::from_slice(&bytes).map_err(|error| {
        anyhow::anyhow!(
            "catalog must be a localized HearthstoneJSON card array (line {}, column {})",
            error.line(),
            error.column()
        )
    })?;
    ensure!(
        !raw.is_empty() && raw.len() <= 50000,
        "catalog requires 1..=50000 cards"
    );
    let mut ids = HashSet::new();
    let mut external_ids = HashSet::new();
    for card in &raw {
        ensure!(
            card.dbf_id > 0
                && ids.insert(card.dbf_id)
                && external_ids.insert(&card.id)
                && bounded(&card.id, 128)
                && bounded(&card.name, 512)
                && card.card_type.as_ref().is_none_or(|kind| bounded(kind, 64))
                && card
                    .card_class
                    .as_ref()
                    .is_none_or(|class| bounded(class, 64))
                && card.cost.is_none_or(|cost| cost <= 1000),
            "invalid or duplicate catalog card identity/metadata"
        );
        ensure!(
            card.classes.len() <= 16
                && card.classes.iter().all(|class| bounded(class, 64))
                && card.classes.iter().collect::<HashSet<_>>().len() == card.classes.len(),
            "invalid catalog class list"
        );
    }
    drop(external_ids);
    let total = raw.len();
    let terms: Vec<_> = request
        .query
        .split_whitespace()
        .map(str::to_lowercase)
        .collect();
    let mut cards: Vec<_> = raw
        .into_iter()
        .filter(|card| {
            if !request.include_non_collectible && !card.collectible {
                return false;
            }
            let searchable = format!(
                "{} {} {} {} {} {}",
                card.name,
                card.id,
                card.dbf_id,
                card.card_class.as_deref().unwrap_or(""),
                card.card_type.as_deref().unwrap_or(""),
                card.classes.join(" ")
            )
            .to_lowercase();
            terms.iter().all(|term| searchable.contains(term))
        })
        .map(|card| Card {
            dbf_id: card.dbf_id,
            id: card.id,
            name: card.name,
            card_type: card.card_type,
            card_class: card.card_class,
            classes: card.classes,
            cost: card.cost,
            collectible: card.collectible,
        })
        .collect();
    cards.sort_by(|left, right| {
        (left.cost.unwrap_or(u32::MAX), &left.name, left.dbf_id).cmp(&(
            right.cost.unwrap_or(u32::MAX),
            &right.name,
            right.dbf_id,
        ))
    });
    let matches = cards.len();
    cards.truncate(100);
    Ok(Response {
        schema_version: 1,
        catalog_cards: total,
        matches,
        cards,
    })
}
pub fn run(args: &[OsString]) -> Result<()> {
    let [path] = args else {
        anyhow::bail!("Usage: hdt-host catalog FILE (search JSON on stdin)");
    };
    let mut request = Vec::new();
    std::io::stdin().take(4097).read_to_end(&mut request)?;
    ensure!(request.len() <= 4096, "catalog request exceeds byte bound");
    let response = search(
        Path::new(path),
        serde_json::from_slice(&request).map_err(|error| {
            anyhow::anyhow!(
                "invalid catalog request (line {}, column {})",
                error.line(),
                error.column()
            )
        })?,
    )?;
    let bytes = serde_json::to_vec(&response)?;
    ensure!(
        bytes.len() <= 1024 * 1024,
        "catalog response exceeds byte bound"
    );
    std::io::stdout().lock().write_all(&bytes)?;
    Ok(())
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn validated_local_catalog_searches_metadata_without_inventing_ownership_or_legality() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("catalog.json");
        let source=br#"[{"dbfId":77,"id":"FIXTURE_A","name":"Illustrative Amber","type":"MINION","cardClass":"MAGE","cost":2,"collectible":true,"ignoredFutureField":{"anything":true}},
        {"dbfId":100,"id":"FIXTURE_B","name":"Illustrative Azure","type":"SPELL","cost":1,"collectible":true},
        {"dbfId":637,"id":"HERO_FIXTURE","name":"Illustrative Hero","type":"HERO","cardClass":"MAGE"},
        {"dbfId":999,"id":"PLACEHOLDER_FIXTURE","name":"Illustrative placeholder","classes":["MAGE","HUNTER"]}]"#;
        std::fs::write(&path, source).unwrap();
        let result = search(
            &path,
            Search {
                query: "illustrative mage".into(),
                include_non_collectible: false,
            },
        )
        .unwrap();
        assert_eq!(result.matches, 1);
        assert_eq!(result.cards[0].dbf_id, 77);
        let result = search(
            &path,
            Search {
                query: "hero".into(),
                include_non_collectible: true,
            },
        )
        .unwrap();
        assert_eq!(result.cards[0].dbf_id, 637);
        assert_eq!(result.cards[0].cost, None);
        let placeholder = search(
            &path,
            Search {
                query: "placeholder hunter".into(),
                include_non_collectible: true,
            },
        )
        .unwrap();
        assert_eq!(
            placeholder.cards[0].card_type, None,
            "actual public placeholders may omit type; absence must remain explicit"
        );
        assert_eq!(
            std::fs::read(&path).unwrap(),
            source,
            "search must never rewrite its external source"
        );
        for malformed in [b"{}".as_slice(),b"[]",br#"[{"dbfId":1,"id":"x","name":{"enUS":"wrong shape"},"type":"SPELL"}]"#,
            br#"[{"dbfId":1,"id":"x","name":"One","type":"SPELL"},{"dbfId":1,"id":"y","name":"Two","type":"SPELL"}]"#] {
            std::fs::write(&path,malformed).unwrap(); assert!(search(&path,Search{query:"".into(),include_non_collectible:false}).is_err());
        }
        std::fs::write(&path, br#"[{"dbfId":"PRIVATE_SENTINEL_DO_NOT_ECHO"}]"#).unwrap();
        let error = search(
            &path,
            Search {
                query: "".into(),
                include_non_collectible: false,
            },
        )
        .err()
        .unwrap();
        assert!(
            !format!("{error:#}").contains("PRIVATE_SENTINEL"),
            "malformed external file values must not be copied into diagnostics"
        );
    }
}
