use anyhow::{Context, Result, bail};
use hdt_host::{config::Config, control, launch, setup, status, x11};

fn run() -> Result<i32> {
    let mut args = std::env::args_os().skip(1);
    let command = args.next().unwrap_or_else(|| "help".into());
    let args: Vec<_> = args.collect();
    match command.to_str().context("Command must be UTF-8")? {
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
                "Usage: hdt-host config|raw-launch|control-input|status|surface-check|install-plugin|configure-overlay|overlay-rule|overlay-link|surface-probe|observe|trace-export"
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
