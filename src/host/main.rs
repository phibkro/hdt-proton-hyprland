use anyhow::{Context, Result, bail};
use hdt_host::{config::Config, control, launch, setup, status, x11};

fn run() -> Result<i32> {
    hdt_host::native_session::bind_owned_parent()?;
    let mut args = std::env::args_os().skip(1);
    let command = args.next().unwrap_or_else(|| "help".into());
    let args: Vec<_> = args.collect();
    match command.to_str().context("Command must be UTF-8")? {
        "catalog" => {
            hdt_host::catalog::run(&args)?;
            Ok(0)
        }
        "native-overlay" => {
            hdt_host::native_overlay::run(&args)?;
            Ok(0)
        }
        "client" => {
            hdt_host::client::run(&args)?;
            Ok(0)
        }
        "native-session" => hdt_host::native_session::run(&args),
        "read-snapshot" => hdt_host::snapshot::run(&args),
        "observe" => {
            hdt_host::journal::run(&args)?;
            Ok(0)
        }
        "trace-export" => {
            hdt_host::trace::run(&args)?;
            Ok(0)
        }
        "overlay-link" => {
            x11::overlay_link(&args)?;
            Ok(0)
        }
        "surface-probe" => {
            x11::surface_probe(&args)?;
            Ok(0)
        }
        "x11-desktop" => {
            hdt_host::desktop::run(&args)?;
            Ok(0)
        }
        "x11-control" => {
            hdt_host::desktop::control(&Config::from_env()?, &args)?;
            Ok(0)
        }
        "config" => {
            let cfg = Config::from_env()?;
            match args.as_slice() {
                [] => println!("{}", serde_json::to_string_pretty(&cfg)?),
                [field] => {
                    let value = serde_json::to_value(&cfg)?;
                    let key = field.to_str().context("Config field must be UTF-8")?;
                    let field = value
                        .get(key)
                        .and_then(serde_json::Value::as_str)
                        .context("Unknown configuration field")?;
                    println!("{field}");
                }
                _ => bail!("Usage: hdt-host config [FIELD]"),
            };
            Ok(0)
        }
        "libxi-check" => {
            anyhow::ensure!(args.len() == 1, "Usage: hdt-host libxi-check LIBRARY");
            println!(
                "{}",
                launch::libxi_library(std::path::Path::new(&args[0]))?.display()
            );
            Ok(0)
        }
        "raw-launch" => launch::run(&Config::from_env()?, &args),
        "control-input" => {
            control::run(&Config::from_env()?, &args)?;
            Ok(0)
        }
        "status" => {
            if !args.is_empty() {
                bail!("Usage: hdt-host status");
            }
            status::run(&Config::from_env()?)?;
            Ok(0)
        }
        "surface-check" => {
            status::surface_check(&Config::from_env()?, &args)?;
            Ok(0)
        }
        "install-plugin" => {
            if !args.is_empty() {
                bail!("Usage: hdt-host install-plugin");
            }
            setup::install_plugin(&Config::from_env()?)?;
            Ok(0)
        }
        "configure-overlay" => {
            if !args.is_empty() {
                bail!("Usage: hdt-host configure-overlay");
            }
            setup::configure_overlay(&Config::from_env()?)?;
            Ok(0)
        }
        "overlay-rule" => {
            let disabled = match args.as_slice() {
                [] => false,
                [arg] if arg == "--disable" => true,
                _ => bail!("Usage: hdt-host overlay-rule [--disable]"),
            };
            setup::overlay_rule(&Config::from_env()?, disabled)
        }
        "help" | "--help" | "-h" => {
            println!(
                "Usage: hdt-host config|libxi-check|raw-launch|control-input|status|surface-check|install-plugin|configure-overlay|overlay-rule|overlay-link|surface-probe|x11-desktop|x11-control|observe|trace-export|read-snapshot|client|native-overlay|native-session|catalog"
            );
            Ok(0)
        }
        other => {
            bail!("Unknown native command: {other}")
        }
    }
}
fn main() {
    match run() {
        Ok(code) => std::process::exit(code),
        Err(error) => {
            eprintln!("HDT host: {error:#}");
            std::process::exit(1)
        }
    }
}
