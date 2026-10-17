namespace Yaya.App.Pet;

/// <summary>
/// 宠物视觉状态。与 Core 的 FocusState 解耦：映射规则在 PetViewModel，
/// 文件名约定在本目录的 PetVisualStateCatalog（换图不改代码的根基）。
/// </summary>
public enum PetVisualState
{
    Healthy = 0,
    Blink = 1,
    WiltA = 2,
    WiltB = 3,
    Weeds = 4,
    Bloom = 5,
}
