using System;

using Rezerwacje.Api.models;
using static System.DayOfWeek;

namespace Rezerwacje.Api.Services;

public static class Dane
{
    public static Lokal Salon() => new()
    {
        Id = "lok1",
        Nazwa = "Barber Salon",
        Typ = "barber",
        Pracownicy =
        [
            new() { Id = "p1", Imie = "Mariusz" },
            new() { Id = "p2", Imie = "Artur" }
        ],
        Godziny =
        [
            new() { Dzien = DayOfWeek.Monday, Od = "09:00", Do = "17:00" },
            new() { Dzien = DayOfWeek.Tuesday, Od = "09:00", Do = "17:00" },
            new() { Dzien = DayOfWeek.Wednesday, Od = "09:00", Do = "17:00" },
            new() { Dzien = DayOfWeek.Thursday, Od = "09:00", Do = "17:00" },
            new() { Dzien = DayOfWeek.Friday, Od = "09:00", Do = "17:00" },
            new() { Dzien = DayOfWeek.Saturday, Od = "10:00", Do = "14:00" }
        ]
    };

    private static readonly DayOfWeek[] PnPt = [Monday, Tuesday, Wednesday, Thursday, Friday];

    public static List<Lokal> Lokale() =>
    [
        Nowy("lok3", "Barber Shop Brodacz", "barber",
            [Prac("p1", "Kamil"), Prac("p2", "Tomasz")],
            Dni("09:00", "18:00", PnPt), Dni("10:00", "14:00", Saturday)),
        Nowy("lok4", "Męski Zakątek", "barber",
            [Prac("p1", "Damian")],
            Dni("10:00", "18:00", Tuesday, Wednesday, Thursday, Friday, Saturday)),
        Nowy("lok5", "Salon Fryzjerski Bella", "fryzjer",
            [Prac("p1", "Katarzyna"), Prac("p2", "Magda")],
            Dni("08:00", "16:00", PnPt)),
        Nowy("lok6", "Studio Włosów Loki", "fryzjer",
            [Prac("p1", "Natalia")],
            Dni("10:00", "18:00", PnPt), Dni("09:00", "13:00", Saturday)),
        Nowy("lok7", "Fryzjer u Basi", "fryzjer",
            [Prac("p1", "Barbara")],
            Dni("09:00", "15:00", Monday, Wednesday, Friday)),
        Nowy("lok8", "Salon Kosmetyczny Aura", "kosmetyczka",
            [Prac("p1", "Joanna"), Prac("p2", "Ewelina")],
            Dni("09:00", "17:00", PnPt)),
        Nowy("lok9", "Gabinet Urody Róża", "kosmetyczka",
            [Prac("p1", "Agnieszka")],
            Dni("11:00", "19:00", Tuesday, Wednesday, Thursday, Friday), Dni("10:00", "14:00", Saturday)),
        Nowy("lok10", "Studio Paznokci Glam", "paznokcie",
            [Prac("p1", "Paulina"), Prac("p2", "Wiktoria")],
            Dni("10:00", "18:00", PnPt), Dni("10:00", "15:00", Saturday)),
        Nowy("lok11", "Nail Bar Perła", "paznokcie",
            [Prac("p1", "Oliwia")],
            Dni("12:00", "20:00", PnPt)),
        Nowy("lok12", "Salon Masażu Relaks", "masaz",
            [Prac("p1", "Piotr"), Prac("p2", "Monika")],
            Dni("08:00", "18:00", PnPt)),
        Nowy("lok13", "Barber Salon Brzytwa", "barber",
            [Prac("p1", "Mateusz"), Prac("p2", "Krzysztof"), Prac("p3", "Bartek")],
            Dni("09:00", "19:00", PnPt), Dni("09:00", "15:00", Saturday)),
        Nowy("lok14", "Studio Fryzur Anna i Ola", "fryzjer",
            [Prac("p1", "Ola")],
            Dni("09:00", "17:00", PnPt)),
        Nowy("lok15", "Kosmetyka Natura", "kosmetyczka",
            [Prac("p1", "Marta")],
            Dni("10:00", "16:00", Monday, Tuesday, Wednesday, Thursday)),
        Nowy("lok16", "Salon Urody Glamour", "kosmetyczka",
            [Prac("p1", "Sylwia"), Prac("p2", "Dominika")],
            Dni("09:00", "17:00", PnPt), Dni("09:00", "13:00", Saturday)),
        Nowy("lok17", "Barber Studio Old School", "barber",
            [Prac("p1", "Adrian")],
            Dni("10:00", "18:00", PnPt)),
    ];
    private static Lokal Nowy(string id, string nazwa, string typ, List<Pracownik> prac, params List<GodzinyOtw>[] godz) => new()
    {
        Id = id,
        Nazwa = nazwa,
        Typ = typ,
        Pracownicy = prac,
        Godziny = [.. godz.SelectMany(x => x)]
    };

    private static Pracownik Prac(string id, string imie) => new() { Id = id, Imie = imie };

    private static List<GodzinyOtw> Dni(string od, string dop, params DayOfWeek[] dni) =>
        [.. dni.Select(d => new GodzinyOtw { Dzien = d, Od = od, Do = dop })];
}