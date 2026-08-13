Set shell = CreateObject("WScript.Shell")
shell.CurrentDirectory = "D:\vertex ERP\services\BiometricReceiver"
shell.Environment("PROCESS")("ASPNETCORE_ENVIRONMENT") = "Development"
receiverPath = "D:\vertex ERP\services\BiometricReceiver\BiometricReceiver.exe"
shell.Run """" & receiverPath & """", 0, False
pythonPath = "C:\easytime\Python\pythonw.exe"
pullerPath = "D:\vertex ERP\services\BiometricReceiver\biometric_puller.py"
shell.Run """" & pythonPath & """ """ & pullerPath & """", 0, False
