using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Westral.App.WtApi;


public class StateModel
{
    public const string Route = "state";

    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("aileron, %")]
    public int Aileron { get; set; }

    [JsonPropertyName("elevator, %")]
    public int Elevator { get; set; }

    [JsonPropertyName("rudder, %")]
    public int Rudder { get; set; }

    [JsonPropertyName("flaps, %")]
    public int Flaps { get; set; }

    [JsonPropertyName("gear, %")]
    public int Gear { get; set; }

    [JsonPropertyName("H, m")]
    public int H { get; set; }

    [JsonPropertyName("TAS, km/h")]
    public int TAS { get; set; }

    [JsonPropertyName("IAS, km/h")]
    public int IAS { get; set; }

    [JsonPropertyName("M")]
    public float M { get; set; }

    [JsonPropertyName("AoA, deg")]
    public float AoA { get; set; }

    [JsonPropertyName("AoS, deg")]
    public float AoS { get; set; }

    [JsonPropertyName("Ny")]
    public float Ny { get; set; }

    [JsonPropertyName("Vy, m/s")]
    public float Vy { get; set; }

    [JsonPropertyName("Wx, deg/s")]
    public int Wx { get; set; }

    [JsonPropertyName("Mfuel, kg")]
    public int Mfuel { get; set; }

    [JsonPropertyName("Mfuel0, kg")]
    public int Mfuel0 { get; set; }

    [JsonPropertyName("Throttle 1, %")]
    public int Throttle1 { get; set; }

    [JsonPropertyName("RPM throttle 1, %")]
    public int RPMthrottle1 { get; set; }

    //public int mixture1 { get; set; }
    //public int radiator1 { get; set; }
    //public int compressorstage1 { get; set; }
    //public int magneto1 { get; set; }

    [JsonPropertyName("power 1, hp")]
    public float Power1 { get; set; }

    //public int RPM1 { get; set; }
    //public float manifoldpressure1atm { get; set; }
    //public int oiltemp1C { get; set; }

    [JsonPropertyName("pitch 1, deg")]
    public float Pitch1deg { get; set; }
    //public int thrust1kgs { get; set; }
    //public int efficiency1 { get; set; }
    //public int throttle2 { get; set; }
    //public int RPMthrottle2 { get; set; }
    //public int mixture2 { get; set; }
    //public int radiator2 { get; set; }
    //public int compressorstage2 { get; set; }
    //public int magneto2 { get; set; }

    [JsonPropertyName("power 2, hp")]
    public float Power2 { get; set; }

    //public int RPM2 { get; set; }
    //public float manifoldpressure2atm { get; set; }
    //public int oiltemp2C { get; set; }

    [JsonPropertyName("pitch 2, deg")]
    public float Pitch2 { get; set; }
    //public int thrust2kgs { get; set; }
    //public int efficiency2 { get; set; }
}
