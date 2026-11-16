#![no_main]
libfuzzer_sys::fuzz_target!(|data: &[u8]| {
    if data.len() <= 65_536
        && let Ok(line) = std::str::from_utf8(data)
    {
        let _ = hdt_host::trace::trace_events([line]);
    }
});
