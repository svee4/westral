using System;
using System.Collections.Generic;
using System.Text;

namespace Westral.App.WtApi;


public class IndicatorsModel
{
    public const string Route = "indicators";

    public bool valid { get; set; }
    public string army { get; set; }
    public string type { get; set; }
    public float speed { get; set; }
    public float pedals { get; set; }
    public float pedals1 { get; set; }
    public float pedals2 { get; set; }
    public float pedals3 { get; set; }
    public float stick_elevator { get; set; }
    public float stick_ailerons { get; set; }
    public float vario { get; set; }
    public float altitude_hour { get; set; }
    public float altitude_min { get; set; }
    public float aviahorizon_roll { get; set; }
    public float aviahorizon_pitch { get; set; }
    public float bank { get; set; }
    public float turn { get; set; }
    public float compass { get; set; }
    public float compass1 { get; set; }
    public float compass2 { get; set; }
    public float clock_hour { get; set; }
    public float clock_min { get; set; }
    public float clock_sec { get; set; }
    public float manifold_pressure { get; set; }
    public float manifold_pressure1 { get; set; }
    public float rpm { get; set; }
    public float rpm1 { get; set; }
    public float oil_pressure { get; set; }
    public float oil_pressure1 { get; set; }
    public float oil_temperature { get; set; }
    public float oil_temperature1 { get; set; }
    public float head_temperature { get; set; }
    public float head_temperature1 { get; set; }
    public float mixture { get; set; }
    public float mixture1 { get; set; }
    public float carb_temperature { get; set; }
    public float carb_temperature1 { get; set; }
    public float fuel { get; set; }
    public float fuel1 { get; set; }
    public float fuel2 { get; set; }
    public float fuel_pressure { get; set; }
    public float fuel_pressure1 { get; set; }
    public float gears { get; set; }
    public float gears2 { get; set; }
    public float gear_lamp_down { get; set; }
    public float gear_lamp_up { get; set; }
    public float gear_lamp_off { get; set; }
    public float flaps { get; set; }
    public float trimmer { get; set; }
    public float throttle { get; set; }
    public float throttle1 { get; set; }
    public float weapon2 { get; set; }
    public float weapon3 { get; set; }
    public float prop_pitch { get; set; }
    public float prop_pitch1 { get; set; }
    public float supercharger { get; set; }
    public float supercharger1 { get; set; }
    public float trimmer_indicator { get; set; }
    public float radiator_lever1_1 { get; set; }
    public float radiator_lever2_1 { get; set; }
    public float oil_radiator_lever1_1 { get; set; }
    public float oil_radiator_lever2_1 { get; set; }
    public float g_meter { get; set; }
    public float blister1 { get; set; }
    public float blister2 { get; set; }
    public float blister3 { get; set; }
    public float blister4 { get; set; }
    public float blister5 { get; set; }
}
