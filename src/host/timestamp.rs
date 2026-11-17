//! Timestamp boundary shared by persisted records, status output and freshness checks.
//!
//! `time` supplies calendar validation and offset arithmetic. The small adapter keeps
//! the former RFC3339 acceptance and Chrono wire formats, including leap seconds:
//! `time` otherwise clamps `:60` to the last nanosecond of `:59`.
//! Large dates permit UTC normalization across year 0000/9999. RFC3339 parsing
//! still requires four-digit years; SystemTime formatting retains the legacy
//! extended-year spelling outside that wire input range.
use anyhow::{Context, Result};
use std::time::{SystemTime, UNIX_EPOCH};
use time::{OffsetDateTime, UtcOffset, format_description::well_known::Rfc3339};

#[derive(Clone, Copy, Debug)]
pub struct Timestamp {
    value: OffsetDateTime,
    leap_second: bool,
}

impl Timestamp {
    pub fn now() -> Self {
        Self {
            value: OffsetDateTime::now_utc(),
            leap_second: false,
        }
    }

    pub fn parse_rfc3339(text: &str) -> Result<Self> {
        let leap_second = text.as_bytes().get(17..19) == Some(b"60");
        let mut normalized;
        let input = if leap_second {
            normalized = text.to_owned();
            normalized.replace_range(17..19, "59");
            normalized.as_str()
        } else {
            text
        };
        let value = OffsetDateTime::parse(input, &Rfc3339).context("Invalid RFC3339 timestamp")?;
        Ok(Self { value, leap_second })
    }

    pub fn from_system_time(value: SystemTime) -> Result<Self> {
        let nanos = match value.duration_since(UNIX_EPOCH) {
            Ok(duration) => i128::try_from(duration.as_nanos())?,
            Err(error) => -i128::try_from(error.duration().as_nanos())?,
        };
        Ok(Self {
            value: OffsetDateTime::from_unix_timestamp_nanos(nanos)?,
            leap_second: false,
        })
    }

    pub fn from_unix_millis(value: i64) -> Result<Self> {
        Ok(Self {
            value: OffsetDateTime::from_unix_timestamp_nanos(i128::from(value) * 1_000_000)?,
            leap_second: false,
        })
    }

    fn unix_nanos(self) -> i128 {
        self.value.unix_timestamp_nanos() + if self.leap_second { 1_000_000_000 } else { 0 }
    }

    pub fn unix_millis(self) -> i64 {
        // time's supported calendar range fits in i64 milliseconds.
        self.unix_nanos().div_euclid(1_000_000) as i64
    }

    /// Signed elapsed milliseconds, truncating submilliseconds toward zero.
    pub fn elapsed_millis_since(self, earlier: Self) -> i128 {
        // Legacy durations count an endpoint leap when the UTC time-of-day
        // crosses its base second, even when the endpoints lie on different dates.
        // This is deliberately distinct from subtraction of Unix timestamps.
        // Behavior reference: Chrono 0.4.45 NaiveTime::signed_duration_since
        // (MIT OR Apache-2.0), src/naive/time/mod.rs.
        let ordering = self
            .value
            .unix_timestamp()
            .rem_euclid(86_400)
            .cmp(&earlier.value.unix_timestamp().rem_euclid(86_400));
        let endpoint_correction = match ordering {
            std::cmp::Ordering::Greater if earlier.leap_second => 1_000_000_000,
            std::cmp::Ordering::Less if self.leap_second => -1_000_000_000,
            _ => 0,
        };
        (self.unix_nanos() - earlier.unix_nanos() + endpoint_correction) / 1_000_000
    }

    pub fn utc_millis(self) -> String {
        let value = self.value.to_offset(UtcOffset::UTC);
        format!("{}.{:03}Z", self.date_seconds(value), value.millisecond())
    }

    /// Preserve the numeric offset and the former automatic 0/3/6/9-digit precision.
    pub fn rfc3339(self) -> String {
        let nanos = self.value.nanosecond();
        let fraction = if nanos == 0 {
            String::new()
        } else if nanos.is_multiple_of(1_000_000) {
            format!(".{:03}", nanos / 1_000_000)
        } else if nanos.is_multiple_of(1_000) {
            format!(".{:06}", nanos / 1_000)
        } else {
            format!(".{nanos:09}")
        };
        let offset = self.value.offset().whole_seconds();
        format!(
            "{}{fraction}{}{:02}:{:02}",
            self.date_seconds(self.value),
            if offset < 0 { '-' } else { '+' },
            offset.abs() / 3600,
            offset.abs() / 60 % 60
        )
    }

    fn date_seconds(self, value: OffsetDateTime) -> String {
        let year = value.year();
        let year = if year < 0 {
            format!("-{:04}", year.unsigned_abs())
        } else if year > 9999 {
            format!("+{year}")
        } else {
            format!("{year:04}")
        };
        format!(
            "{}-{:02}-{:02}T{:02}:{:02}:{:02}",
            year,
            u8::from(value.month()),
            value.day(),
            value.hour(),
            value.minute(),
            if self.leap_second { 60 } else { value.second() }
        )
    }
}

#[cfg(test)]
mod tests {
    use super::Timestamp;
    use std::time::{Duration, UNIX_EPOCH};

    #[test]
    fn accepts_existing_records_and_preserves_their_instant_and_wire_precision() {
        // Explicit legacy and .NET inputs; expected values were checked against
        // the removed Chrono 0.4.45 parser, not generated by this implementation.
        for (input, millis, output) in [
            ("1970-01-01T00:00:00Z", 0, "1970-01-01T00:00:00+00:00"),
            (
                "1969-12-31T23:59:59.999999999Z",
                -1,
                "1969-12-31T23:59:59.999999999+00:00",
            ),
            (
                "2026-11-17T14:00:00.1234567Z",
                1794924000123,
                "2026-11-17T14:00:00.123456700+00:00",
            ),
            (
                "2026-11-17T14:00:00.123456789123+01:30",
                1794918600123,
                "2026-11-17T14:00:00.123456789+01:30",
            ),
            (
                "2026-11-17 14:00:00z",
                1794924000000,
                "2026-11-17T14:00:00+00:00",
            ),
            (
                "2026-11-17t14:00:00-00:00",
                1794924000000,
                "2026-11-17T14:00:00+00:00",
            ),
            (
                "2016-12-31T23:59:60.123456789Z",
                1483228800123,
                "2016-12-31T23:59:60.123456789+00:00",
            ),
            (
                "0000-01-01T00:00:00Z",
                -62167219200000,
                "0000-01-01T00:00:00+00:00",
            ),
            (
                "9999-12-31T23:59:59Z",
                253402300799000,
                "9999-12-31T23:59:59+00:00",
            ),
        ] {
            let parsed = Timestamp::parse_rfc3339(input).unwrap();
            assert_eq!(parsed.unix_millis(), millis, "{input}");
            assert_eq!(parsed.rfc3339(), output, "{input}");
        }
    }

    #[test]
    fn rejects_invalid_calendar_or_non_rfc3339_boundary_input() {
        for input in [
            "",
            "2026-02-29T00:00:00Z",
            "2026-11-17",
            "2026-11-17T14:00:00",
            "2026-11-17T14:00:61Z",
            "2026-11-17T24:00:00Z",
            "2026-11-17T14:00:00+24:00",
            "2026-11-17T14:00:00Z[UTC]",
            "2026-11-17T14:00:00Z trailing",
            "+10000-01-01T00:00:00Z",
        ] {
            assert!(Timestamp::parse_rfc3339(input).is_err(), "{input}");
        }
    }

    #[test]
    fn status_and_journal_keep_utc_milliseconds_including_pre_epoch_files() {
        assert_eq!(
            Timestamp::from_system_time(UNIX_EPOCH)
                .unwrap()
                .utc_millis(),
            "1970-01-01T00:00:00.000Z"
        );
        assert_eq!(
            Timestamp::from_system_time(UNIX_EPOCH - Duration::from_nanos(1))
                .unwrap()
                .utc_millis(),
            "1969-12-31T23:59:59.999Z"
        );
        assert_eq!(
            Timestamp::parse_rfc3339("2026-11-17T14:00:00.1234567+01:30")
                .unwrap()
                .utc_millis(),
            "2026-11-17T12:30:00.123Z"
        );
        assert!(Timestamp::from_unix_millis(i64::MAX).is_err());
    }

    #[test]
    fn extended_system_dates_and_offset_rollover_keep_legacy_formatting() {
        assert_eq!(
            Timestamp::from_unix_millis(-62198755200000)
                .unwrap()
                .utc_millis(),
            "-0001-01-01T00:00:00.000Z"
        );
        assert_eq!(
            Timestamp::parse_rfc3339("9999-12-31T23:59:59-23:59")
                .unwrap()
                .utc_millis(),
            "+10000-01-01T23:58:59.000Z"
        );
        assert_eq!(
            Timestamp::parse_rfc3339("0000-01-01T00:00:00+01:30")
                .unwrap()
                .utc_millis(),
            "-0001-12-31T22:30:00.000Z"
        );
    }

    #[test]
    fn leap_elapsed_counts_endpoint_crossings_in_utc_in_both_directions() {
        // Fixed legacy results, including the review counterexample. Epoch
        // subtraction alone produces zero rather than 1000 for the first pair.
        for (later, earlier, millis) in [
            ("2016-12-31T12:01:00Z", "2016-12-31T12:00:60Z", 1000),
            (
                "2016-12-31T12:01:00.001Z",
                "2016-12-31T12:00:60.9999999Z",
                1,
            ),
            ("2017-01-01T12:01:00Z", "2016-12-31T12:00:60Z", 86401000),
            ("2017-01-01T00:00:00Z", "2016-12-31T23:59:60Z", 0),
            ("2016-12-31T12:01:60Z", "2016-12-31T12:00:60Z", 61000),
            ("2017-01-01T12:01:60Z", "2016-12-31T12:00:60Z", 86461000),
            ("2016-12-31T12:00:60.9Z", "2016-12-31T12:00:60.1Z", 800),
            (
                "2016-12-31T13:01:00+01:00",
                "2016-12-31T07:00:60-05:00",
                1000,
            ),
            (
                "2017-01-01T13:01:00+01:00",
                "2016-12-31T07:00:60-05:00",
                86401000,
            ),
        ] {
            let later = Timestamp::parse_rfc3339(later).unwrap();
            let earlier = Timestamp::parse_rfc3339(earlier).unwrap();
            assert_eq!(later.elapsed_millis_since(earlier), millis);
            assert_eq!(earlier.elapsed_millis_since(later), -millis);
        }
    }

    #[test]
    fn freshness_preserves_signed_truncation_and_inclusive_vs_exclusive_boundaries() {
        let leap = Timestamp::parse_rfc3339("2016-12-31T23:59:60.123Z").unwrap();
        for (input, expected) in [
            ("2017-01-01T00:00:00Z", -123),
            ("2016-12-31T23:59:59Z", -1123),
            ("2017-01-01T00:00:01Z", 877),
        ] {
            assert_eq!(
                Timestamp::parse_rfc3339(input)
                    .unwrap()
                    .elapsed_millis_since(leap),
                expected
            );
        }
        let now = Timestamp::parse_rfc3339("2026-11-17T14:00:00Z").unwrap();
        for (input, expected) in [
            ("2026-11-17T13:59:58.0000001Z", 1999),
            ("2026-11-17T13:59:58Z", 2000),
            ("2026-11-17T13:59:57.500Z", 2500),
            ("2026-11-17T13:59:57.499Z", 2501),
            ("2026-11-17T14:00:00.0000001Z", 0),
            ("2026-11-17T14:00:00.001Z", -1),
        ] {
            assert_eq!(
                now.elapsed_millis_since(Timestamp::parse_rfc3339(input).unwrap()),
                expected
            );
        }
    }
}
