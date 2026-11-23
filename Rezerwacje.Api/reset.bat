@echo off
docker exec bazka mongosh rezerwacje --quiet --eval "db.rezerwacje.deleteMany({})"
docker exec redis redis-cli DEL wolne:lok1:p1:2026-12-16 wolne:lok1:p2:2026-12-16 rezerwacje:klient:k1 rezerwacje:klient:k2
echo.
echo  Baza oczyszczona
docker exec bazka mongosh rezerwacje --quiet --eval "db.rezerwacje.find()"