#![no_main]
libfuzzer_sys::fuzz_target!(|data: &[u8]| {
    if data.len() <= 70_000 {
        let _ = hdt_host::desktop::validate_input_frame(data);
    }
});
