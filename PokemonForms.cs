namespace DeskPokemon;

/// <summary>
/// pokerogue-assets에서 기본형 파일명이 숫자만이 아니라 폼 이름을 포함하는 33종의 기본 폼.
/// 아이콘 아틀라스 프레임명과 본체 스프라이트 파일명(pokemon/{key}.json)이 같은 키를 씀.
/// </summary>
internal static class PokemonForms
{
    private static readonly Dictionary<int, string> DefaultForm = new()
    {
        [201] = "a", [412] = "plant", [413] = "plant", [421] = "overcast", [422] = "west", [423] = "west",
        [487] = "altered", [492] = "land", [493] = "normal", [550] = "red-striped", [585] = "spring", [586] = "spring",
        [641] = "incarnate", [642] = "incarnate", [645] = "incarnate", [647] = "ordinary", [648] = "aria",
        [666] = "meadow", [669] = "red", [670] = "red", [671] = "red", [716] = "active", [778] = "disguised",
        [905] = "incarnate", [925] = "four", [931] = "green-plumage", [964] = "zero", [978] = "curly",
        [1007] = "apex-build", [1008] = "ultimate-mode", [1012] = "counterfeit", [1013] = "unremarkable", [1017] = "teal-mask",
    };

    /// <summary>"964-zero" 또는 "4".</summary>
    public static string SpriteKey(int dex) =>
        DefaultForm.TryGetValue(dex, out var form) ? $"{dex}-{form}" : dex.ToString();
}
