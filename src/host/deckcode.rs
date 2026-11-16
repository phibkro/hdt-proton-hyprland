//! Independent codec for the public deckstring format, including sideboards.
//! Format reference: MIT HearthDb DeckSerializer.cs at 4f91601ac3a47fa06eede3eac5c4cf84db52545e.
use anyhow::{Context, Result, bail, ensure};
use base64::Engine;
use serde::{Deserialize, Serialize};
use std::collections::BTreeMap;

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct CardCount {
    pub dbf_id: u32,
    pub count: u32,
}
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct SideboardCard {
    pub owner_dbf_id: u32,
    pub dbf_id: u32,
    pub count: u32,
}
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct DeckContent {
    pub hero_dbf_id: u32,
    pub format: u32,
    pub cards: Vec<CardCount>,
    pub sideboards: Vec<SideboardCard>,
}
impl DeckContent {
    pub fn validate(&self) -> Result<()> {
        ensure!(
            self.hero_dbf_id > 0 && (1..=4).contains(&self.format),
            "deck requires a positive hero DBF ID and known format 1..4"
        );
        ensure!(
            self.cards.len() <= 100 && self.sideboards.len() <= 100,
            "deck collection exceeds preview bound"
        );
        let mut main = BTreeMap::new();
        let mut total = 0_u32;
        for card in &self.cards {
            ensure!(
                card.dbf_id > 0 && (1..=100).contains(&card.count),
                "invalid card ID or count"
            );
            ensure!(
                main.insert(card.dbf_id, card.count).is_none(),
                "duplicate main-deck ID"
            );
            total = total
                .checked_add(card.count)
                .context("card count overflow")?;
        }
        ensure!(total <= 100, "deck exceeds 100 cards");
        let mut side = BTreeMap::new();
        for card in &self.sideboards {
            ensure!(
                main.contains_key(&card.owner_dbf_id)
                    && card.dbf_id > 0
                    && (1..=100).contains(&card.count),
                "invalid sideboard owner, ID or count"
            );
            ensure!(
                side.insert((card.owner_dbf_id, card.dbf_id), card.count)
                    .is_none(),
                "duplicate sideboard ID"
            );
        }
        Ok(())
    }
}
fn write(value: u32, bytes: &mut Vec<u8>) {
    let mut value = value;
    while value >= 128 {
        bytes.push((value as u8 & 127) | 128);
        value >>= 7;
    }
    bytes.push(value as u8);
}
fn read(bytes: &[u8], offset: &mut usize) -> Result<u32> {
    let mut result = 0_u32;
    for shift in (0..35).step_by(7) {
        let value = *bytes.get(*offset).context("truncated deckstring")?;
        *offset += 1;
        ensure!(shift != 28 || value <= 15, "deckstring integer overflows");
        result |= u32::from(value & 127) << shift;
        if value & 128 == 0 {
            return Ok(result);
        }
    }
    bail!("overlong deckstring integer")
}
pub fn encode(deck: &DeckContent) -> Result<String> {
    deck.validate()?;
    let mut bytes = vec![0, 1];
    write(deck.format, &mut bytes);
    write(1, &mut bytes);
    write(deck.hero_dbf_id, &mut bytes);
    let mut cards = deck.cards.clone();
    cards.sort_by_key(|card| card.dbf_id);
    for copies in [1, 2, 3] {
        let group: Vec<_> = cards
            .iter()
            .filter(|c| {
                if copies < 3 {
                    c.count == copies
                } else {
                    c.count > 2
                }
            })
            .collect();
        write(group.len() as u32, &mut bytes);
        for card in group {
            write(card.dbf_id, &mut bytes);
            if copies == 3 {
                write(card.count, &mut bytes);
            }
        }
    }
    write(u32::from(!deck.sideboards.is_empty()), &mut bytes);
    if !deck.sideboards.is_empty() {
        let mut sideboards = deck.sideboards.clone();
        sideboards.sort_by_key(|card| (card.dbf_id, card.owner_dbf_id));
        for copies in [1, 2, 3] {
            let group: Vec<_> = sideboards
                .iter()
                .filter(|c| {
                    if copies < 3 {
                        c.count == copies
                    } else {
                        c.count > 2
                    }
                })
                .collect();
            write(group.len() as u32, &mut bytes);
            for card in group {
                write(card.dbf_id, &mut bytes);
                write(card.owner_dbf_id, &mut bytes);
                if copies == 3 {
                    write(card.count, &mut bytes);
                }
            }
        }
    }
    Ok(base64::engine::general_purpose::STANDARD.encode(bytes))
}
pub fn decode(input: &str) -> Result<DeckContent> {
    ensure!(input.len() <= 65536, "deck import exceeds 64 KiB");
    let lines: Vec<_> = input
        .lines()
        .map(str::trim)
        .filter(|line| !line.is_empty() && !line.starts_with('#'))
        .collect();
    ensure!(
        lines.len() == 1,
        "import requires exactly one deckstring, optionally with # comments"
    );
    let bytes = base64::engine::general_purpose::STANDARD
        .decode(lines[0])
        .context("invalid base64 deckstring")?;
    ensure!(bytes.first() == Some(&0), "unsupported deckstring header");
    let mut offset = 1;
    ensure!(
        read(&bytes, &mut offset)? == 1,
        "unsupported deckstring version"
    );
    let format = read(&bytes, &mut offset)?;
    ensure!(
        read(&bytes, &mut offset)? == 1,
        "deckstring must have one hero"
    );
    let hero_dbf_id = read(&bytes, &mut offset)?;
    let mut cards = Vec::new();
    for copies in [1, 2, 3] {
        let length = read(&bytes, &mut offset)?;
        ensure!(length <= 100, "deckstring card group exceeds bound");
        for _ in 0..length {
            let dbf_id = read(&bytes, &mut offset)?;
            let count = if copies == 3 {
                read(&bytes, &mut offset)?
            } else {
                copies
            };
            cards.push(CardCount { dbf_id, count });
        }
    }
    let mut sideboards = Vec::new();
    if offset < bytes.len() {
        let flag = read(&bytes, &mut offset)?;
        ensure!(flag <= 1, "unsupported sideboard marker");
        if flag == 1 {
            for copies in [1, 2, 3] {
                let length = read(&bytes, &mut offset)?;
                ensure!(length <= 100, "sideboard group exceeds bound");
                for _ in 0..length {
                    let dbf_id = read(&bytes, &mut offset)?;
                    let owner_dbf_id = read(&bytes, &mut offset)?;
                    let count = if copies == 3 {
                        read(&bytes, &mut offset)?
                    } else {
                        copies
                    };
                    sideboards.push(SideboardCard {
                        owner_dbf_id,
                        dbf_id,
                        count,
                    });
                }
            }
        }
    }
    ensure!(
        offset == bytes.len(),
        "unsupported trailing deckstring data"
    );
    cards.sort_by_key(|card| card.dbf_id);
    sideboards.sort_by_key(|card| (card.dbf_id, card.owner_dbf_id));
    let deck = DeckContent {
        hero_dbf_id,
        format,
        cards,
        sideboards,
    };
    deck.validate()?;
    Ok(deck)
}
