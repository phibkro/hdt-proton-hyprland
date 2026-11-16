//! Host parser baseline; does not measure Wine/WPF animation performance.
use criterion::{BenchmarkId, Criterion, Throughput, criterion_group, criterion_main};
use hdt_host::desktop::validate_input_frame;
use std::{hint::black_box, time::Duration};

fn input_protocol(c: &mut Criterion) {
    let mut group = c.benchmark_group("input-protocol");
    group.sample_size(20);
    group.warm_up_time(Duration::from_millis(100));
    group.measurement_time(Duration::from_millis(300));
    for count in [1, 32, 512, 2048] {
        let wire = format!("v1 3440 1440 {count}{}", " 10 10 160 40".repeat(count));
        validate_input_frame(wire.as_bytes()).unwrap();
        group.throughput(Throughput::Bytes(wire.len() as u64));
        group.bench_with_input(BenchmarkId::from_parameter(count), &wire, |b, wire| {
            b.iter(|| validate_input_frame(black_box(wire.as_bytes())).unwrap());
        });
    }
    group.finish();
}
criterion_group!(benches, input_protocol);
criterion_main!(benches);
