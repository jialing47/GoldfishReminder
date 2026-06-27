@echo off
chcp 65001 >nul
REM 一鍵發佈：dotnet publish -> 移除 Development 設定 -> 打包 goldfish-app.zip
REM %~dp0 = 這支 .bat 所在資料夾（repo 根，結尾自帶反斜線）不寫死絕對路徑
cd /d "%~dp0"

REM 清掉舊的 publish 內容 對齊發佈設定檔的「刪除現有檔案=true」
if exist publish rmdir /s /q publish

REM 發佈 Api 專案到 publish 對齊 FolderProfile：Release / linux-x64 / 自帶執行階段 / 單一執行檔
dotnet publish GoldfishReminder.Api\GoldfishReminder.Api.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o publish
if errorlevel 1 (
    echo 發佈失敗 已中止 不打包
    pause
    exit /b 1
)

cd publish

REM 移除 Development 設定 避免本機密鑰或開發設定上到伺服器
del /q appsettings.Development.json 2>nul

REM 打包成 goldfish-app.zip 資料夾結構維持不變方便覆蓋
tar -a -cf ..\goldfish-app.zip *
cd ..

pause
