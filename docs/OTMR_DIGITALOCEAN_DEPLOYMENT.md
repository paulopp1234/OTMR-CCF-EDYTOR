# OTMR server deployment on DigitalOcean

This runbook targets Ubuntu 24.04 on a DigitalOcean Droplet. The server stores permanent historical evidence in SQLite at `/var/lib/otmr/OTMR_RCM.db`. The database file and its WAL files are never served by Nginx and no database port is opened.

## 1. Host and restricted account

Create a non-login service account and owned directories:

```bash
sudo adduser --system --group --home /var/lib/otmr otmr
sudo install -d -o otmr -g otmr -m 0750 /var/lib/otmr
sudo install -d -o root -g root -m 0755 /opt/otmr/server
sudo install -d -o root -g otmr -m 0750 /etc/otmr
```

Keep SSH key authentication enabled and disable password/root login according to the host security policy. Configure UFW so only SSH, HTTP, and HTTPS are public:

```bash
sudo ufw allow OpenSSH
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw enable
```

Do not open port 5080 publicly.

## 2. Install the .NET 8 runtime

Use Microsoft's current Ubuntu 24.04 package instructions, then install `aspnetcore-runtime-8.0`. Verify it with `dotnet --info`. The SDK is required on the build machine, not necessarily on the Droplet.

## 3. Publish and install

On a controlled build machine:

```bash
dotnet publish src/CcfEditor.Otmr.Server/CcfEditor.Otmr.Server.csproj -c Release -r linux-x64 --self-contained false -o ./publish/otmr-server
```

Transfer the publish output over SSH to a staging directory, then install it under `/opt/otmr/server`. Keep application binaries owned by root and read-only to the `otmr` account. Install [the supplied systemd unit](../deploy/otmr-server.service) as `/etc/systemd/system/otmr-server.service`.

## 4. Secrets and settings

Generate a high-entropy bearer token outside source control. Put it in `/etc/otmr/otmr-server.env`, readable only by root and the service group:

```text
OtmrServer__BearerToken=replace-with-a-random-production-token
OtmrServer__MaximumUploadBodyBytes=134217728
OtmrServer__MaximumQueryResultCount=1000
OtmrServer__MaximumSessionResultCount=100
OtmrServer__MaximumRecordRangeDays=31
```

Then:

```bash
sudo chown root:otmr /etc/otmr/otmr-server.env
sudo chmod 0640 /etc/otmr/otmr-server.env
sudo systemctl daemon-reload
sudo systemctl enable --now otmr-server
sudo systemctl status otmr-server
curl http://127.0.0.1:5080/health
```

The service binds to `127.0.0.1:5080` only. A blank token fails closed: authenticated API calls receive 401.

## 5. Nginx and HTTPS

Install Nginx, copy [the supplied site template](../deploy/nginx-otmr.conf) to `/etc/nginx/sites-available/otmr`, replace `otmr.example.com` with the real DNS name, enable it, and validate it with `sudo nginx -t`. The template proxies only to Kestrel on loopback and applies a 128 MiB request-body limit.

After the DNS A/AAAA record resolves, install Certbot's Nginx plugin and obtain a Let's Encrypt certificate. Enable HTTP-to-HTTPS redirection. Production upload and read traffic must use HTTPS; do not send bearer tokens over HTTP. Test renewal with `sudo certbot renew --dry-run`.

## 6. Database ownership and operation

`/var/lib/otmr` must remain owned by `otmr:otmr` with mode 0750. SQLite runs in WAL mode with foreign keys enabled and a 5-second busy timeout. Uploads use short transactions, and an upload receipt is committed in the same transaction as all session evidence.

Never copy a live `.db`, `-wal`, or `-shm` file independently. Never expose the directory through Nginx or a file-sharing service.

## 7. WAL-safe backup and restore

For an online logical backup, install `sqlite3` and use its backup API, which captures a consistent database while WAL mode is active:

```bash
sudo -u otmr sqlite3 /var/lib/otmr/OTMR_RCM.db "PRAGMA wal_checkpoint(PASSIVE);"
sudo -u otmr sqlite3 /var/lib/otmr/OTMR_RCM.db ".backup '/var/lib/otmr/OTMR_RCM.backup.db'"
```

Copy the completed backup file to encrypted off-host storage and test restores regularly. For the simplest maintenance-window backup, stop `otmr-server`, run `PRAGMA wal_checkpoint(TRUNCATE)`, copy the single database file, and restart the service. Verify the resolved source and destination paths before copying or restoring. Restore only while the service is stopped; retain the displaced database until integrity and session counts are verified.

## 8. Updating and verification

Before deployment, run the repository Release build and full tests. Upload new binaries to a staging directory, stop the service, replace `/opt/otmr/server`, then restart it. Check `/health`, systemd logs, an authenticated read call, disk capacity, and backup status. Do not use the health endpoint to expose paths, tokens, connection strings, or OTMR evidence.

Atomic completed-session upload is historical evidence synchronization. It is not realtime telemetry. The `/live` response remains unavailable until a separate genuine live-state ingestion design is implemented.
