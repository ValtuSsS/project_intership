@echo off
if "%~2"=="" (
    echo Uzycie: wyscig.bat plikA.json plikB.json
    exit /b
)
del wA.txt wB.txt 2>nul

start /b curl -s -i -X POST http://localhost:5126/rezerwacje -H "Content-Type: application/json" -H "X-Klient-Id: k1" -d @%1 -o wA.txt
start /b curl -s -i -X POST http://localhost:5127/rezerwacje -H "Content-Type: application/json" -H "X-Klient-Id: k2" -d @%2 -o wB.txt

timeout /t 5 /nobreak >nul

echo 5126 / k1 / %1
findstr /B /C:"HTTP" /C:"{" wA.txt
echo.
echo 5127 / k2 / %2
findstr /B /C:"HTTP" /C:"{" wB.txt
echo.
echo Baza:
docker exec bazka mongosh rezerwacje --quiet --eval "db.rezerwacje.find({}, { KlientId: 1 })"