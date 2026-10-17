namespace Yaya.App.Pet;

/// <summary>
/// PetVisualState → 文件名 的唯一映射表（文件名约定集中维护，XAML 与代码一律经此解析）。
/// </summary>
public static class PetVisualStateCatalog
{
    private static readonly IReadOnlyDictionary<PetVisualState, string> FileNames =
        new Dictionary<PetVisualState, string>
        {
            [PetVisualState.Healthy] = "pet_healthy.png",
            [PetVisualState.Blink] = "pet_blink.png",
            [PetVisualState.WiltA] = "pet_wilt_a.png",
            [PetVisualState.WiltB] = "pet_wilt_b.png",
            [PetVisualState.Weeds] = "pet_weeds.png",
            [PetVisualState.Bloom] = "pet_bloom.png",
        };

    public static string FileNameFor(PetVisualState state) => FileNames[state];
}
