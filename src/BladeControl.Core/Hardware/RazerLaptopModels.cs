using BladeControl.Core.Models;

namespace BladeControl.Core.Hardware;

/// <summary>
/// Every Razer laptop this app knows how to find. To add one: plug it in with
/// nothing else Razer-branded attached, open Device Manager, find the
/// keyboard-adjacent HID device under this vendor, and read the product id
/// (the four hex digits after "PID_") from its Hardware Ids. It is safe to
/// add an unverified entry; see <see cref="RazerLaptopModel.Verified"/>.
/// </summary>
internal static class RazerLaptopModels
{
    public static readonly IReadOnlyList<RazerLaptopModel> Known =
    [
        new RazerLaptopModel(0x029F, "Razer Blade 16 (2023)", Verified: true),

        // Community-reported product ids, not verified on this app: the
        // command set is the one documented for the Blade 16 (2023) above,
        // tried the same way on these (see RazerCommands). A command the
        // firmware does not implement fails cleanly rather than guessing.
        // From sqmagellan/razer-ctl's published device-support table.
        new RazerLaptopModel(0x028A, "Razer Blade 15 (2022)", Verified: false),
        new RazerLaptopModel(0x029D, "Razer Blade 14 (2023) Mercury", Verified: false),
        new RazerLaptopModel(0x02B7, "Razer Blade 16 (2024)", Verified: false),
        new RazerLaptopModel(0x02C6, "Razer Blade 16 (2025)", Verified: false),

        // The other Blades from 2021 on, by the product ids OpenRazer's
        // keyboard driver lists for them. Same command set, same caveat: not
        // verified here, and what a firmware lacks is refused cleanly.
        new RazerLaptopModel(0x026D, "Razer Blade 15 Advanced (Early 2021)", Verified: false),
        new RazerLaptopModel(0x026E, "Razer Blade 17 Pro (Early 2021)", Verified: false),
        new RazerLaptopModel(0x026F, "Razer Blade 15 Base (Early 2021)", Verified: false),
        new RazerLaptopModel(0x0270, "Razer Blade 14 (2021)", Verified: false),
        new RazerLaptopModel(0x0276, "Razer Blade 15 Advanced (Mid 2021)", Verified: false),
        new RazerLaptopModel(0x0279, "Razer Blade 17 Pro (Mid 2021)", Verified: false),
        new RazerLaptopModel(0x027A, "Razer Blade 15 Base (2022)", Verified: false),
        new RazerLaptopModel(0x028B, "Razer Blade 17 (2022)", Verified: false),
        new RazerLaptopModel(0x028C, "Razer Blade 14 (2022)", Verified: false),
        new RazerLaptopModel(0x029E, "Razer Blade 15 (2023)", Verified: false),
        new RazerLaptopModel(0x02A0, "Razer Blade 18 (2023)", Verified: false),
        new RazerLaptopModel(0x02B6, "Razer Blade 14 (2024)", Verified: false),
        new RazerLaptopModel(0x02B8, "Razer Blade 18 (2024)", Verified: false),
        new RazerLaptopModel(0x02C5, "Razer Blade 14 (2025)", Verified: true), // Confirmed by a user on their own.
        new RazerLaptopModel(0x02C7, "Razer Blade 18 (2025)", Verified: false),
        new RazerLaptopModel(0x02E0, "Razer Blade 16 (2026)", Verified: false),
        new RazerLaptopModel(0x02E1, "Razer Blade 18 (2026)", Verified: false),

        // Tested by a user on their own RZ09-0328: performance modes (Gaming
        // included), Custom boost levels, lighting (its keyboard is a single
        // zone, so no Wave) and the controller temperature work. Its firmware
        // echoes every class 0x07
        // read and write without acting on it, so the charge limit and the
        // max fan flag only look accepted; max fan speed works through the
        // older manual fan method instead (not in Silent). It has no fan speed
        // command (0x0D88).
        new RazerLaptopModel(
            0x0255,
            "Razer Blade 15 Base (2020)",
            Verified: false,
            HasChargeLimit: false,
            MaxFan: MaxFanMethod.ManualFan,
            HasKeyboardColor: true,
            HasFanSpeeds: false,
            HasWaveEffect: false),
    ];
}
