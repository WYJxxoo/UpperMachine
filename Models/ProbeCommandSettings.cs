namespace UpperMachine.Models;

public sealed class ProbeCommandSettings
{
    public string RaiseCommand { get; set; } = "$J=G91 Z-10 F5000";

    public string DropCommand { get; set; } = "$J=G91 Z10 F5000";

    public string HoldCommand { get; set; } = "$J=G90 Z0.0 F500";

    public string Step { get; set; } = "1";

    public string Feed { get; set; } = "500";

    public ProbeCommandSettings Clone()
    {
        return new ProbeCommandSettings
        {
            RaiseCommand = RaiseCommand,
            DropCommand = DropCommand,
            HoldCommand = HoldCommand,
            Step = Step,
            Feed = Feed,
        };
    }
}
