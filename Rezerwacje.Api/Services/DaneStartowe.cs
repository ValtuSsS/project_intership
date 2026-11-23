using System;

using Rezerwacje.Api.models;
using static System.DayOfWeek;

namespace Rezerwacje.Api.Services;

public static class DaneStartowe
{
    private static readonly DayOfWeek[] DniRobocze = [Monday, Tuesday, Wednesday, Thursday, Friday];

    public static Lokal SalonStartowy()
    {
        return UtworzLokal("lok1", "Barber Salon", "barber",
            [UtworzPracownika("p1", "Mariusz"), UtworzPracownika("p2", "Artur")],
            UtworzGodzinyOtwarcia("09:00", "17:00", DniRobocze), UtworzGodzinyOtwarcia("10:00", "14:00", Saturday));
    }

    public static List<Lokal> LokaleStartowe()
    {
        return
        [
            UtworzLokal("lok2", "Salon Fryzjerski Anna", "fryzjer",
                [UtworzPracownika("p1", "Anna")],
                UtworzGodzinyOtwarcia("10:00", "18:00", Monday)),
            UtworzLokal("lok3", "Barber Shop Brodacz", "barber",
                [UtworzPracownika("p1", "Kamil"), UtworzPracownika("p2", "Tomasz")],
                UtworzGodzinyOtwarcia("09:00", "18:00", DniRobocze), UtworzGodzinyOtwarcia("10:00", "14:00", Saturday)),
            UtworzLokal("lok4", "Męski Zakątek", "barber",
                [UtworzPracownika("p1", "Damian")],
                UtworzGodzinyOtwarcia("10:00", "18:00", Tuesday, Wednesday, Thursday, Friday, Saturday)),
            UtworzLokal("lok5", "Salon Fryzjerski Bella", "fryzjer",
                [UtworzPracownika("p1", "Katarzyna"), UtworzPracownika("p2", "Magda")],
                UtworzGodzinyOtwarcia("08:00", "16:00", DniRobocze)),
            UtworzLokal("lok6", "Studio Włosów Loki", "fryzjer",
                [UtworzPracownika("p1", "Natalia")],
                UtworzGodzinyOtwarcia("10:00", "18:00", DniRobocze), UtworzGodzinyOtwarcia("09:00", "13:00", Saturday)),
            UtworzLokal("lok7", "Fryzjer u Basi", "fryzjer",
                [UtworzPracownika("p1", "Barbara")],
                UtworzGodzinyOtwarcia("09:00", "15:00", Monday, Wednesday, Friday)),
            UtworzLokal("lok8", "Salon Kosmetyczny Aura", "kosmetyczka",
                [UtworzPracownika("p1", "Joanna"), UtworzPracownika("p2", "Ewelina")],
                UtworzGodzinyOtwarcia("09:00", "17:00", DniRobocze)),
            UtworzLokal("lok9", "Gabinet Urody Róża", "kosmetyczka",
                [UtworzPracownika("p1", "Agnieszka")],
                UtworzGodzinyOtwarcia("11:00", "19:00", Tuesday, Wednesday, Thursday, Friday), UtworzGodzinyOtwarcia("10:00", "14:00", Saturday)),
            UtworzLokal("lok10", "Studio Paznokci Glam", "paznokcie",
                [UtworzPracownika("p1", "Paulina"), UtworzPracownika("p2", "Wiktoria")],
                UtworzGodzinyOtwarcia("10:00", "18:00", DniRobocze), UtworzGodzinyOtwarcia("10:00", "15:00", Saturday)),
            UtworzLokal("lok11", "Nail Bar Perła", "paznokcie",
                [UtworzPracownika("p1", "Oliwia")],
                UtworzGodzinyOtwarcia("12:00", "20:00", DniRobocze)),
            UtworzLokal("lok12", "Salon Masażu Relaks", "masaz",
                [UtworzPracownika("p1", "Piotr"), UtworzPracownika("p2", "Monika")],
                UtworzGodzinyOtwarcia("08:00", "18:00", DniRobocze)),
            UtworzLokal("lok13", "Barber Salon Brzytwa", "barber",
                [UtworzPracownika("p1", "Mateusz"), UtworzPracownika("p2", "Krzysztof"), UtworzPracownika("p3", "Bartek")],
                UtworzGodzinyOtwarcia("09:00", "19:00", DniRobocze), UtworzGodzinyOtwarcia("09:00", "15:00", Saturday)),
            UtworzLokal("lok14", "Studio Fryzur Anna i Ola", "fryzjer",
                [UtworzPracownika("p1", "Ola")],
                UtworzGodzinyOtwarcia("09:00", "17:00", DniRobocze)),
            UtworzLokal("lok15", "Kosmetyka Natura", "kosmetyczka",
                [UtworzPracownika("p1", "Marta")],
                UtworzGodzinyOtwarcia("10:00", "16:00", Monday, Tuesday, Wednesday, Thursday)),
            UtworzLokal("lok16", "Salon Urody Glamour", "kosmetyczka",
                [UtworzPracownika("p1", "Sylwia"), UtworzPracownika("p2", "Dominika")],
                UtworzGodzinyOtwarcia("09:00", "17:00", DniRobocze), UtworzGodzinyOtwarcia("09:00", "13:00", Saturday)),
            UtworzLokal("lok17", "Barber Studio Old School", "barber",
                [UtworzPracownika("p1", "Adrian")],
                UtworzGodzinyOtwarcia("10:00", "18:00", DniRobocze)),
        ];
    }

    private static Lokal UtworzLokal(
        string id,
        string nazwa,
        string typ,
        List<Pracownik> pracownicy,
        params List<GodzinyOtwarcia>[] godzinyOtwarcia)
    {
        return new Lokal
        {
            Id = id,
            Nazwa = nazwa,
            Typ = typ,
            Pracownicy = pracownicy,
            GodzinyOtwarcia = [.. godzinyOtwarcia.SelectMany(listaGodzin => listaGodzin)]
        };
    }

    private static Pracownik UtworzPracownika(string id, string imie)
    {
        return new Pracownik { Id = id, Imie = imie };
    }

    private static List<GodzinyOtwarcia> UtworzGodzinyOtwarcia(string odGodziny, string doGodziny, params DayOfWeek[] dni)
    {
        var godzinyOtwarcia = new List<GodzinyOtwarcia>();
        foreach (var dzien in dni)
        {
            godzinyOtwarcia.Add(new GodzinyOtwarcia
            {
                DzienTygodnia = dzien,
                OdGodziny = odGodziny,
                DoGodziny = doGodziny
            });
        }

        return godzinyOtwarcia;
    }
}
