namespace CcfEditor.Core;

public static class CcfFieldDefinitions
{
    public static class Record
    {
        public const int EventIndex = 0x00;
        public const int Type = 0x02;
        public const int ClassificationFlag = 0x03;
        public const int Name = 0x04;
        public const int NameLength = 16;
        public const int Colour = 0x14;
        public const int ColourLength = 4;
        public const int TypeDependentStart = 0x18;
        public const int TypeDependentLength = 64;
        public const int Card = 0x58;
        public const int Channel = 0x59;
        public const int LoggerMode = 0x5A;
        public const int HardwareFunction = 0x5B;
        public const int TypeSpecificTail = 0x5C;
        public const int TypeSpecificTailLength = 8;
    }

    public static class Digital
    {
        public const int OffDescription = 0x18;
        public const int OffDescriptionLength = 16;
        public const int OnDescription = 0x28;
        public const int OnDescriptionLength = 16;
        public const int PairRecord = 0x38;
    }

    public static class Numeric
    {
        public const int Min = 0x18;
        public const int Max = 0x1C;
        public const int Offset = 0x20;
        public const int Gain = 0x24;
        public const int Units = 0x38;
        public const int UnitsLength = 16;
        public const int DecimalPlaces = 0x48;
    }

    public static class Header
    {
        public const int ProfileFamilyWord = 0x002E;
        public const int FirmwareVersion = 0x01B0;
        public const int SerialCore = 0x01B8;
        public const int Vehicle = 0x01C0;
        public const int Unit = 0x01CA;
        public const int VehicleType = 0x01D1;
        public const int CardsFitted = 0x01F9;
        public const int Jp6 = 0x01FA;
        public const int Jp12 = 0x0202;
        public const int Mileage = 0x0228;
        public const int Wheel1 = 0x022C;
        public const int Wheel2 = 0x022E;
        public const int PulsesPerRev = 0x0230;
        public const int Unknown0231 = 0x0231;
        public const int Poll = 0x025C;
        public const int NoEvent = 0x025D;
        public const int SampleCount = 0x025E;
        public const int DistanceTrigger = 0x025F;
        public const int MidJourney = 0x0261;
    }
}
