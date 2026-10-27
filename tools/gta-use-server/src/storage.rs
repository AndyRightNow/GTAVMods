use serde_json::{Value, json};
use std::{
    fs::{self, File, OpenOptions},
    io::{self, Read, Write},
    path::{Path, PathBuf},
    sync::Mutex,
    time::{Duration, Instant, SystemTime, UNIX_EPOCH},
};

pub fn read_json(path: &Path) -> io::Result<Value> {
    let deadline = Instant::now() + Duration::from_millis(200);
    loop {
        // Rust's Windows File::open shares READ, WRITE and DELETE, so the game
        // can atomically replace a receipt while an external agent reads it.
        let read = (|| {
            let mut data = Vec::new();
            File::open(path)?
                .take(4 * 1024 * 1024 + 1)
                .read_to_end(&mut data)?;
            if data.len() > 4 * 1024 * 1024 {
                return Err(io::Error::other("JSON artifact exceeds 4 MiB"));
            }
            let data = data.strip_prefix(&[0xef, 0xbb, 0xbf]).unwrap_or(&data);
            serde_json::from_slice(data).map_err(io::Error::other)
        })();
        match read {
            Err(ref error)
                if matches!(
                    error.kind(),
                    io::ErrorKind::NotFound | io::ErrorKind::PermissionDenied
                ) && Instant::now() < deadline =>
            {
                std::thread::sleep(Duration::from_millis(5))
            }
            result => return result,
        }
    }
}

pub fn save_json(path: &Path, value: &Value) -> io::Result<()> {
    let temporary = path.with_extension("json.tmp");
    let mut file = File::create(&temporary)?;
    serde_json::to_writer(&mut file, value)?;
    file.sync_all()?;
    drop(file);
    // On Windows rename uses MoveFileExW(REPLACE_EXISTING).
    fs::rename(temporary, path)
}

pub struct Journal {
    path: PathBuf,
    writer: Mutex<File>,
}
impl Journal {
    pub fn new(path: PathBuf) -> io::Result<Self> {
        let writer = Mutex::new(OpenOptions::new().append(true).create(true).open(&path)?);
        Ok(Self { path, writer })
    }
    pub fn record(&self, event: &str, data: Value) -> io::Result<()> {
        let mut file = self
            .writer
            .lock()
            .map_err(|_| io::Error::other("Journal lock poisoned"))?;
        if file.metadata()?.len() >= 5 * 1024 * 1024 {
            for index in (1..=9).rev() {
                let destination = self.path.with_extension(format!("jsonl.{index}"));
                let source = if index == 1 {
                    self.path.clone()
                } else {
                    self.path.with_extension(format!("jsonl.{}", index - 1))
                };
                if source.exists() {
                    fs::rename(source, destination)?;
                }
            }
            *file = OpenOptions::new()
                .append(true)
                .create(true)
                .open(&self.path)?;
        }
        let utc = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap_or_default()
            .as_secs_f64();
        let line = serde_json::to_vec(&json!({"utc":utc,"event":event,"data":data}))?;
        file.write_all(&line)?;
        file.write_all(b"\n")?;
        file.flush()
    }
}
