# Deploy both Vertex ERP projects to an Azure Windows VM

## Architecture

`Biometric machine -> Azure VM port 8082 -> Azure PostgreSQL -> Vertex ERP website`

The VM has two applications: the main **Vertex ERP** website and the **BiometricReceiver** Windows Service. Do not run `biometric_puller.py` on the VM: it can only reach a machine on the office LAN. The biometric device must use ADMS push to the VM public IP.

## 1. Create and secure the VM

1. Create a Windows Server 2022 Azure VM with a **static public IP**.
2. In its Network Security Group, allow RDP (3389) only from your admin public IP.
3. Add an inbound rule for TCP **8082**, source restricted to the office router's public IP. Do not use `Any` as the source.
4. Install the .NET 8 Hosting Bundle and IIS on the VM.
5. In Azure PostgreSQL networking, allow the VM's outbound public IP.

## 2. Publish the main ERP website

1. In Visual Studio, right-click **Vertex ERP** > **Publish** > Folder.
2. Copy the published files to `C:\VertexERP\Web` on the VM. This publish now includes the complete receiver DLLs and files in `C:\VertexERP\Web\BiometricReceiver`.
3. In IIS, create an Application Pool using **No Managed Code**, then create a site pointing to `C:\VertexERP\Web`.
4. Bind the site to port 80/443 and configure an HTTPS certificate for your domain.
5. Put the Azure PostgreSQL connection string in the site's `appsettings.Production.json` or an IIS environment variable; do not commit it to source control.
6. Start the IIS site and verify the ERP login page.

## 3. Publish the biometric receiver

1. No separate Visual Studio publish is required: use the `BiometricReceiver` folder created inside the Vertex ERP publish output.
2. Copy `C:\VertexERP\Web\BiometricReceiver` to `C:\VertexERP\BiometricReceiver` on the VM.
3. Copy `appsettings.Production.example.json` as `appsettings.Production.json` and set:
   - `DefaultConnection`: Azure PostgreSQL connection string.
   - `AllowedSourceIps`: office router's public IP.
4. Run PowerShell **as Administrator** on the VM:

```powershell
Set-Location C:\VertexERP\BiometricReceiver
.\Install-BiometricReceiver-Service.ps1 -PublishDirectory C:\VertexERP\BiometricReceiver
```

5. Open `http://localhost:8082/health` on the VM. It must show `status: running`.

## 4. Change the biometric machine

Use the VM's **static public IP** in the machine's Cloud Server Setting:

- Server Mode: `ADMS`
- Enable Domain Name: `OFF`
- Server Address: VM static public IP
- Server Port: `8082`
- HTTPS: `OFF`

Save and restart the machine. Its public source IP must match `AllowedSourceIps` and the Azure Network Security Group rule.

## 5. Verify attendance

1. Make a test punch.
2. On the VM, examine `C:\VertexERP\BiometricReceiver\logs\incoming-requests.log`.
3. In ERP, confirm the device's exact serial number is registered and active, then check Attendance.

If the receiver returns `401`, verify the office router public IP has not changed; use a static IP service or update both the NSG rule and `AllowedSourceIps`.
