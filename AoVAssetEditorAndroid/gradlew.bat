@echo off
setlocal
set "GRADLE_VERSION=8.9"
if not defined GRADLE_USER_HOME set "GRADLE_USER_HOME=%USERPROFILE%\.gradle"
set "AOV_GRADLE_HOME=%GRADLE_USER_HOME%\aov-wrapper"
set "AOV_GRADLE_DIST=%AOV_GRADLE_HOME%\gradle-%GRADLE_VERSION%"
if exist "%AOV_GRADLE_DIST%\bin\gradle.bat" goto run_gradle
if not exist "%AOV_GRADLE_HOME%" mkdir "%AOV_GRADLE_HOME%"
set "AOV_GRADLE_ZIP=%AOV_GRADLE_HOME%\gradle-%GRADLE_VERSION%-bin.zip"
echo Downloading official Gradle %GRADLE_VERSION% distribution...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; Invoke-WebRequest 'https://services.gradle.org/distributions/gradle-%GRADLE_VERSION%-bin.zip' -OutFile '%AOV_GRADLE_ZIP%'; Expand-Archive -LiteralPath '%AOV_GRADLE_ZIP%' -DestinationPath '%AOV_GRADLE_HOME%' -Force; Remove-Item -LiteralPath '%AOV_GRADLE_ZIP%' -Force"
if errorlevel 1 exit /b %errorlevel%
:run_gradle
call "%AOV_GRADLE_DIST%\bin\gradle.bat" %*
exit /b %errorlevel%
