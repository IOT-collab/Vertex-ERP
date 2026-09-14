# Attendance on the Azure VM

The current ERP imports EasyTime transactions directly with RemoteAttendanceImportService.
CloudAttendanceForwarding is a separate ADMS path and is not required for this importer.
Do not enable both paths just to resolve an import failure.

## Deploy

1. Back up the VM's current publish directory and settings. Stop its IIS app pool while replacing the application files.
2. Copy the new publish output into the existing site. Preserve the VM's connection string, App_Data, uploads, and production settings. Do not copy a local database configuration over the VM configuration.
3. If per-VM attendance settings are needed, place a JSON object with a RemoteAttendance section in App_Data/remoteattendance.json. Use Enabled, BaseUrl, Username, Password, PollIntervalSeconds and PageSize from the existing working installation. Keep this private file on the VM. Environment variables (RemoteAttendance__Enabled etc.) take precedence. Restart after changing settings.
4. Give the IIS app-pool identity access to the application's required App_Data folders and database. The attendance importer itself no longer needs to write a checkpoint file into the program directory.
5. For continuous IIS hosting, install Application Initialization, set the site's application preloadEnabled=true, its app pool startMode=AlwaysRunning and idleTimeout=00:00:00. Open the site after deployment. These are VM/IIS settings, not settings that a folder publish applies automatically.

## Verify on the VM

- Run `Test-NetConnection 122.176.49.74 -Port 8082`. This tests the attendance source, not the ERP inbound website port. If it fails, check VM outbound rules, routing and the attendance source's firewall/access policy for the VM's public egress IP.
- Log in as Admin or HR and open `/BiometricDevices`. Attendance import shows the last attempt, successful sync, newly imported punches and a safe explanation of failures. Refresh to see updated status. `/BiometricDevices/SyncStatus` provides the same information as JSON, requiring the same login.
- If the connection works but login fails, compare the effective VM RemoteAttendance settings with the working local settings. Check the server logs for the underlying exception.
- If sync is connected but employees still have no punches, confirm the date, destination database and employee/device mappings. A successful poll can import zero records.
- Create a test punch on the machine and check the same employee/date in the Azure attendance page after the configured polling interval.

Progress is now derived from REMOTE attendance rows in the destination database for this source host. A one-day overlap recovers delayed transactions; existing transaction hashes prevent reimporting those same records. A fresh database starts with the available source history. Old remote-attendance-checkpoint.json files are no longer read.

This deployment is only verified end to end once the VM reports a successful sync and a known source punch appears in its attendance page.
