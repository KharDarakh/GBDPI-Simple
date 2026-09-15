@ECHO OFF
PUSHD "%~dp0"
echo GoodbyeDPI Manager derleniyor...
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /platform:anycpu /win32manifest:app.manifest /win32icon:app.ico /out:GoodbyeDPI-Manager.exe /r:System.dll,System.Drawing.dll,System.Windows.Forms.dll,System.ServiceProcess.dll,System.Core.dll GoodbyeDPIManager.cs

IF %ERRORLEVEL% EQU 0 (
    echo.
    echo [BASARILI] GoodbyeDPI-Manager.exe basariyla olusturuldu!
) ELSE (
    echo.
    echo [HATA] Derleme sirasinda hata olustu!
)
POPD
pause
