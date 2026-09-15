@ECHO OFF
PUSHD "%~dp0"
echo Masaustune "GoodbyeDPI Manager" kisayolu olusturuluyor...
powershell -NoProfile -Command "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut([Environment]::GetFolderPath('Desktop') + '\GoodbyeDPI Manager.lnk'); $s.TargetPath = '%CD%\GoodbyeDPI-Manager.exe'; $s.WorkingDirectory = '%CD%'; $s.IconLocation = '%CD%\app.ico,0'; $s.Description = 'GoodbyeDPI Turkiye Kontrol Merkezi ve Sistem Tepsisi Yoneticisi'; $s.Save()"

IF %ERRORLEVEL% EQU 0 (
    echo.
    echo [BASARILI] Masaustunuze kisayol eklendi!
) ELSE (
    echo.
    echo [HATA] Kisayol olusturulamadi.
)
POPD
exit /b 0
