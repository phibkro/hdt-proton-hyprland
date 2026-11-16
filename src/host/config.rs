use std::path::PathBuf;
#[derive(Debug, Clone, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Config {
    pub source_root: PathBuf,
    pub project_root: PathBuf,
    pub steam_root: PathBuf,
    pub prefix: PathBuf,
    pub hdt_data: PathBuf,
    pub app_directory: PathBuf,
    pub native_directory: PathBuf,
    pub app_id: String,
    pub window_class: String,
}
