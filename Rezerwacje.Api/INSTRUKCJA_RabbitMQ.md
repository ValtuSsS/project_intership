# Instrukcja: dodanie RabbitMQ do projektu Rezerwacje.Api

Pracujesz z praktykantem nad istniejącym projektem. Najpierw przeczytaj całą instrukcję, potem obecny kod, dopiero potem pisz.

---

## 1. Zasady pracy (obowiązkowe)

- **Bez komentarzy w kodzie.**
- **Polskie nazwy, które mówią, co się dzieje.** Żadnych skrótów (`lok`, `ust`, `przep`, `x`). Dobre przykłady z projektu: `kluczBlokady`, `czyLokalPobrany`, `pobierzZBazy`, `wolneGodziny`. W lambdach też pełne nazwy: `lokal => lokal.Typ`.
- **Klamry zamiast skróconych zapisów.** Metody mają zwykłe ciało `{ }` z `return`, a nie `=>` na kilka linii. Zamiast `warunek ? a : b` przy zwracaniu wyników piszemy `if` z `return`. Klamry przy każdym `if`, `for`, `foreach`, także jednolinijkowym.
- **Czytelność ponad zwięzłość.** Kod ma być zrozumiały za trzy miesiące dla innej osoby. Pośrednie wyniki w nazwanych zmiennych.
- **Wszędzie `async`/`await`.** Nigdy `.Result` ani `.Wait()`.
- **Styl jak w oficjalnych tutorialach RabbitMQ dla .NET** (rabbitmq.com/tutorials, wersja dla RabbitMQ.Client 7, z API asynchronicznym: `CreateConnectionAsync`, `CreateChannelAsync`, `BasicPublishAsync` itd.).
- **Najprostsze rozwiązanie dające wymagany efekt.** Bez dodatkowych warstw, wzorców i bibliotek, o które nikt nie prosił.
- **Nie zmieniaj istniejącej logiki** (Redis, blokady, cache, Mongo, endpointy, adresy URL). Dopisujesz tylko to, co potrzebne do kolejki.
- **Nie uruchamiaj kodu ani testów.** Użytkownik buduje i testuje sam.
- **Pliki `.cs` mają windowsowe końce linii (CRLF).** Zachowaj je.
- **Odpowiadaj po polsku.** Przy każdej zmianie napisz krótko: który plik, gdzie wkleić, co robi i dlaczego. Podawaj całe pliki albo dokładne miejsce wstawienia. Tłumacz prosto: użytkownik dopiero uczy się C#.

---

## 2. Obecny stan projektu

**Technologie:** ASP.NET Core, .NET 10, kontrolery (`[ApiController]`), MongoDB.Driver, StackExchange.Redis. Repozytorium: `ValtuSsS/project_intership`, folder `Rezerwacje.Api`.

**Środowisko lokalne (Docker):**
- MongoDB: kontener `bazka`, `localhost:27017`, baza `rezerwacje`, kolekcje `lokale` i `rezerwacje`,
- Redis: kontener `redis`, `localhost:6379`, służy tylko jako cache (cache-aside) i do blokad rozproszonych,
- aplikacja działa w dwóch instancjach naraz: A na porcie 5126, B na 5127.

**Struktura:**

```
Rezerwacje.Api/
├── Program.cs                 rejestracja serwisów (singletony), AddControllers, CORS,
│                              middleware z nagłówkiem X-Pid, dane startowe, endpointy /test/...
├── appsettings.json           sekcje: Baza, Redis, Cache
├── Controllers/
│   ├── LokaleController.cs    GET /lokale, GET /lokale/{lokalId}, POST /lokale,
│   │                          PUT /lokale/{lokalId}, GET /lokale/{lokalId}/pracownicy/{pracownikId}/wolne
│   └── RezerwacjeController.cs POST /rezerwacje, GET /rezerwacje/moje
├── Filtry/
│   └── WymaganyKlientIdAttribute.cs  400, gdy brak nagłówka X-Klient-Id
├── Services/
│   ├── BlokadaRedis.cs        blokada SET NX + zwolnienie skryptem Lua
│   ├── CacheRedis.cs          PobierzZCacheLubBazyAsync<T>, UsunZCacheAsync
│   ├── LokaleService.cs       operacje na lokalach (Mongo + cache), DodajAsync zwraca bool
│   ├── RezerwacjeService.cs   wolne godziny, rezerwacja, moje rezerwacje
│   ├── Format.cs              walidacja daty i godziny
│   └── DaneStartowe.cs        lokale wstawiane przy starcie (lok1-lok17)
├── Models/
│   ├── Lokal.cs               klasa Lokal: Id, Nazwa, Typ, Pracownicy, GodzinyOtwarcia
│   ├── Pracownik.cs           Id, Imie
│   ├── GodzinyOtwarcia.cs     DzienTygodnia, OdGodziny, DoGodziny
│   ├── Rezerwacja.cs, NowaRezerwacja.cs, WynikRezerwacji.cs
│   └── UstawieniaCache.cs, UstawieniaBazy.cs   klasy ustawień z appsettings (IOptions)
└── wwwroot/
    ├── demo.html              strona testowa; ma sekcję „Nowy lub edytowany lokal” z przyciskiem Dodaj (POST /lokale)
    └── szukaj.html            wyszukiwanie lokali
```

**Ważne szczegóły obecnego kodu:**
- Serwisy są rejestrowane jako singletony i dostają zależności w konstruktorze. Pola mają nazwy z podkreśleniem: `_lokaleService`, `_ustawienia`, `_log`.
- Ustawienia są w `appsettings.json` i w klasach `Ustawienia...` wczytywanych przez `builder.Services.Configure<...>(builder.Configuration.GetSection("..."))`.
- Logi mają format `_log.LogInformation("[{Pid}] ...", Environment.ProcessId, ...)`.
- `POST /rezerwacje` w `RezerwacjeController.Zarezerwuj` woła `_rezerwacjeService.RezerwujAsync`. Gdy `wynikRezerwacji.Rezerwacja` nie jest null, zwraca 201 z rezerwacją; inaczej błąd z `wynikRezerwacji.Kod`.
- Id lokali: `lok1` do `lok17`; pracownicy numerowani od `p1` osobno w każdym lokalu.

---

## 3. Wymagania (od mentora)

Mentor pokazał to na przykładzie pizzerii. U nas **zamówienie to rezerwacja**, a **pizzeria to lokal z pracownikami**. Klient rezerwuje w lokalu u konkretnego pracownika (np. lok5, p1). Blokada i rezerwacja już teraz są liczone na pracownika lokalu (klucz `lock:{lokalId}:{pracownikId}:{data}`), więc kolejka działa tak samo.

**Zasada najważniejsza:** rezerwację dostaje tylko i wyłącznie ten pracownik tego lokalu, którego wybrał klient. Nikt inny jej nie dostaje: ani inni pracownicy tego lokalu, ani inne lokale, ani żadna wspólna kolejka, ani żadna kopia. Jedna rezerwacja = jedna wiadomość = jedna kolejka jednego pracownika.

1. **RabbitMQ lokalnie w Dockerze**, w wersji z panelem zarządzania. Bez Azure i bez Service Bus.
2. **Klient z NuGeta:** `RabbitMQ.Client`.
3. **Wysyłanie:** po udanej rezerwacji (`POST /rezerwacje`, odpowiedź 201) API wysyła rezerwację jako JSON do RabbitMQ. Przy 400, 404, 409 i 503 nic nie wysyła.
4. **Każdy pracownik lokalu ma swoją kolejkę.** Rezerwacja trafia tylko do pracownika, którego wybrał klient. Rezerwacja u lok5 p1 nie trafia do lok5 p2 ani do lok1 p1.
5. **System lokalu dla pracownika:** nowa aplikacja konsolowa, czyli osobny proces. Uruchamiana z id lokalu i pracownika, np. `dotnet run -- lok5 p1`. Uruchomienie oznacza, że system lokalu się włączył (np. o 9 rano) i przyjmuje rezerwacje.
6. **Zasada `Gotowe`:** w ustawieniach programu jest `Gotowe`. Gdy `Gotowe` = 1, program odbiera rezerwacje. Gdy ma inną wartość, program pisze, że system lokalu jest wyłączony, i nic nie pobiera z kolejki. Pracownik niczego nie wpisuje ręcznie.
7. **Odbieranie:** po włączeniu program wypisuje po kolei wszystkie rezerwacje, które czekały na tego pracownika, w kolejności złożenia, a potem działa dalej i wypisuje nowe na bieżąco, aż zostanie zamknięty.
8. **Gdy program jest wyłączony,** rezerwacje czekają w kolejce i pojawią się przy następnym uruchomieniu.
9. **Rezerwacja nie może zginąć:** jeśli program zostanie zamknięty, zanim wypisze rezerwację, ta rezerwacja zostaje w kolejce.

---

## 4. Rozwiązanie techniczne do zastosowania

- **Docker:** `docker run -d --name rabbit -p 5672:5672 -p 15672:15672 rabbitmq:management`. Panel: `http://localhost:15672`, login i hasło `guest` / `guest`.
- **Ustawienia API:** nowa sekcja w `appsettings.json`, np. `"RabbitMq": { "Host": "localhost", "NazwaExchange": "rezerwacje" }`, i klasa `UstawieniaRabbitMq` w `Models`, wczytywana tak jak pozostałe ustawienia.
- **Exchange** typu `direct` o nazwie `rezerwacje`, trwały (durable).
- **Kolejki** trwałe, osobna na każdego pracownika lokalu: `rezerwacje.lok5.p1`, `rezerwacje.lok5.p2`, `rezerwacje.lok1.p1` itd. Każda powiązana z exchange kluczem `{lokalId}.{pracownikId}`, np. `lok5.p1`.
- **Wiadomości trwałe** (persistent), treść: JSON rezerwacji (id, klient, lokal, pracownik, data, godzina, data utworzenia).
- **Producent deklaruje exchange i kolejkę pracownika przed wysłaniem.** Inaczej rezerwacja wysłana, zanim program tego pracownika był kiedykolwiek uruchomiony, przepadnie. Deklaracje w RabbitMQ są idempotentne, więc można je powtarzać.
- **Producent w API:** nowy serwis, np. `Services/WysylkaRezerwacji.cs`, z jednym połączeniem do RabbitMQ na cały proces (singleton), metodą typu `WyslijRezerwacjeAsync(Rezerwacja rezerwacja)`. Klucz i nazwa kolejki biorą się z `rezerwacja.LokalId` i `rezerwacja.PracownikId`. Wołany w `RezerwacjeController.Zarezerwuj` po udanej rezerwacji, czyli po zapisie w bazie i zwolnieniu blokady. Kod blokady i cache w `RezerwacjeService` zostaje bez zmian.
- **Gdy RabbitMQ nie działa:** rezerwacja i tak zostaje zapisana w bazie i API zwraca 201, a błąd wysyłki trafia do logu (`LogError`). Rezerwacja nie może się wywracać przez kolejkę.
- **Program pracownika:** nowy projekt konsolowy obok API, np. `Rezerwacje.Pracownik`. Argumenty: id lokalu i id pracownika. Plik `appsettings.json` programu: `{ "Host": "localhost", "NazwaExchange": "rezerwacje", "Gotowe": 1 }`.
  - Na starcie sprawdza `Gotowe`. Gdy nie jest 1, wypisuje „System lokalu wyłączony” i kończy, bez łączenia z kolejką.
  - Gdy jest 1, deklaruje to samo exchange, kolejkę i powiązanie co producent, ustawia `BasicQosAsync` z `prefetchCount: 1` (rezerwacje po kolei, jedna naraz) i zaczyna odbierać przez `AsyncEventingBasicConsumer` i `BasicConsumeAsync` z `autoAck: false`, jak w tutorialu „Work Queues”.
  - Dla każdej wiadomości: wypisanie rezerwacji, potem `BasicAckAsync`. Niepotwierdzona wiadomość po zamknięciu programu wraca do kolejki.
  - Program działa, dopóki pracownik go nie zamknie (np. „Naciśnij Enter, aby wyłączyć system lokalu”, jak w tutorialu).
- **Log w API** po wysłaniu, np. `[{Pid}] lok5|p1|2026-12-16|10:00: wysłana do kolejki rezerwacje.lok5.p1`.

---

## 5. Czego nie robić

- nie dodawać Azure, Service Bus ani Azure Functions,
- nie używać exchange typu `fanout` ani `topic` i żadnych symboli wieloznacznych w kluczach (`*`, `#`),
- nie tworzyć wspólnej kolejki dla lokalu, typu ani wszystkich rezerwacji, nie wysyłać kopii rezerwacji nigdzie indziej,
- program pracownika łączy się tylko ze swoją kolejką `rezerwacje.{lokalId}.{pracownikId}` i nie deklaruje ani nie czyta żadnych innych,
- nie zmieniać logiki Redis, blokad, cache i zapytań do Mongo,
- nie zmieniać adresów endpointów ani formatu odpowiedzi,
- nie dodawać komentarzy w kodzie,
- nie wprowadzać skrótów w nazwach,
- nie uruchamiać testów ani aplikacji.

---

## 6. Kolejność pracy

1. Polecenie Docker dla RabbitMQ i wyjaśnienie panelu.
2. Ustawienia: sekcja w `appsettings.json` i klasa `UstawieniaRabbitMq`.
3. Serwis wysyłający w API i rejestracja w `Program.cs`.
4. Wywołanie wysyłki w `RezerwacjeController.Zarezerwuj`.
5. Projekt programu pracownika: utworzenie (`dotnet new console`), dodanie pakietu, `appsettings.json` z `Gotowe`, kod.
6. Instrukcja testu dla użytkownika (poniżej).

Po każdym kroku zatrzymaj się, wyjaśnij krótko zmianę i poczekaj, aż użytkownik potwierdzi.

---

## 7. Jak użytkownik ma to sprawdzić

1. RabbitMQ działa, panel otwiera się pod `http://localhost:15672`.
2. Uruchomione API (A i B jak zwykle).
3. Na `demo.html` robi kilka rezerwacji u lok5 p1, jedną u lok5 p2 i jedną u lok1 p1 (różne godziny). W panelu RabbitMQ widać wiadomości czekające w `rezerwacje.lok5.p1`, `rezerwacje.lok5.p2` i `rezerwacje.lok1.p1`.
4. `Gotowe` = 1, `dotnet run -- lok5 p1`: program wypisuje po kolei tylko rezerwacje lok5 p1. Rezerwacji p2 i lok1 nie ma.
5. Program dalej działa. Nowa rezerwacja u lok5 p1 na `demo.html` pojawia się w nim od razu.
6. `dotnet run -- lok5 p2` pokazuje tylko rezerwację lok5 p2.
7. Program lok5 p1 zamknięty, dwie nowe rezerwacje u lok5 p1: czekają w panelu i pojawiają się po ponownym uruchomieniu.
8. `Gotowe` = 0: program pisze „System lokalu wyłączony”, a rezerwacje zostają w kolejce.
