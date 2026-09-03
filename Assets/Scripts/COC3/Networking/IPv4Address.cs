using System;

namespace CircuitCrafter.Networking
{
    /// <summary>
    /// A single IPv4 address, held as the 32-bit number it actually is rather
    /// than as the dotted string people type.
    ///
    /// NO UnityEngine REFERENCE ANYWHERE IN THIS FILE, deliberately. Everything
    /// in this folder is plain C#, which means the addressing rules can be
    /// unit-tested without opening Unity, and reused unchanged by COC4's network
    /// faults later.
    ///
    /// WHY THIS EXISTS AT ALL
    /// COC3 currently compares typed text against one hardcoded answer, so
    /// 192.168.1.11 fails a step whose key says .10 even though both are
    /// perfectly valid for that subnet. Comparing strings can only ever ask "did
    /// you type the expected characters". Comparing numbers lets you ask the
    /// questions that actually matter: is this in the right subnet, is it the
    /// network address, is it already taken. That is the difference between
    /// checking an answer key and teaching addressing.
    /// </summary>
    public struct IPv4Address : IEquatable<IPv4Address>
    {
        /// <summary>The address as a big-endian 32-bit value: 192.168.1.10 is
        /// 0xC0A8010A.</summary>
        public uint Value { get; private set; }

        public IPv4Address(uint value)
        {
            Value = value;
        }

        public IPv4Address(byte a, byte b, byte c, byte d)
        {
            Value = (uint)((a << 24) | (b << 16) | (c << 8) | d);
        }

        public static readonly IPv4Address None = new IPv4Address(0u);

        public bool IsUnspecified => Value == 0u;

        // ------------------------------------------------------------------
        // Parsing and formatting
        // ------------------------------------------------------------------

        /// <summary>
        /// Strict dotted-quad parse. Rejects everything a learner might type that
        /// looks close but isn't: too few or too many octets, an octet above 255,
        /// empty octets from a double dot, leading or trailing dots, and any
        /// non-digit character.
        ///
        /// Leading zeros are rejected too ("192.168.001.10"). They are ambiguous
        /// - some tools read a leading zero as octal - and no Windows dialog will
        /// show you an address written that way, so accepting them would teach a
        /// habit the real tools don't.
        /// </summary>
        public static bool TryParse(string text, out IPv4Address address)
        {
            address = None;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string trimmed = text.Trim();
            string[] parts = trimmed.Split('.');
            if (parts.Length != 4) return false;

            uint value = 0;

            foreach (string part in parts)
            {
                if (part.Length == 0 || part.Length > 3) return false;
                if (part.Length > 1 && part[0] == '0') return false;   // no leading zeros

                int octet = 0;
                foreach (char c in part)
                {
                    if (c < '0' || c > '9') return false;
                    octet = octet * 10 + (c - '0');
                }

                if (octet > 255) return false;

                value = (value << 8) | (uint)octet;
            }

            address = new IPv4Address(value);
            return true;
        }

        public override string ToString()
        {
            return $"{(Value >> 24) & 0xFF}.{(Value >> 16) & 0xFF}.{(Value >> 8) & 0xFF}.{Value & 0xFF}";
        }

        // ------------------------------------------------------------------
        // Subnet arithmetic
        // ------------------------------------------------------------------

        /// <summary>
        /// True if this value is a legal subnet mask: an unbroken run of 1 bits
        /// followed by an unbroken run of 0 bits.
        ///
        /// This is the check that catches 255.255.0.255 and 255.0.255.0 - masks
        /// that parse perfectly as addresses and are complete nonsense as masks.
        /// Learners produce them constantly by editing one octet at a time.
        /// </summary>
        public bool IsValidSubnetMask()
        {
            // Adding 1 to a contiguous mask rolls it to a single high bit (or to
            // zero for /0), so the AND with the original is 0. Any gap in the run
            // leaves a bit standing.
            uint inverted = ~Value;
            return (inverted & (inverted + 1)) == 0;
        }

        /// <summary>How many leading 1 bits - the /24 in 192.168.1.0/24. Only
        /// meaningful on a valid mask.</summary>
        public int PrefixLength()
        {
            int count = 0;
            uint v = Value;
            while (v != 0)
            {
                count += (int)(v & 1u);
                v >>= 1;
            }
            return count;
        }

        public IPv4Address NetworkAddress(IPv4Address mask) => new IPv4Address(Value & mask.Value);

        public IPv4Address BroadcastAddress(IPv4Address mask) => new IPv4Address(Value | ~mask.Value);

        /// <summary>The all-zeros host part - 192.168.1.0 on a /24. Never usable
        /// by a host.</summary>
        public bool IsNetworkAddress(IPv4Address mask) => Value == (Value & mask.Value);

        /// <summary>The all-ones host part - 192.168.1.255 on a /24. Never usable
        /// by a host.</summary>
        public bool IsBroadcastAddress(IPv4Address mask) => Value == (Value | ~mask.Value);

        public static bool SameSubnet(IPv4Address a, IPv4Address b, IPv4Address mask)
        {
            return (a.Value & mask.Value) == (b.Value & mask.Value);
        }

        /// <summary>How many addresses a host could actually use on this mask -
        /// the usual "2^hostbits minus 2". Returns 0 for /31 and /32, where the
        /// subtraction would go negative.</summary>
        public static int UsableHostCount(IPv4Address mask)
        {
            int hostBits = 32 - mask.PrefixLength();
            if (hostBits < 2) return 0;
            if (hostBits >= 31) return int.MaxValue;
            return (1 << hostBits) - 2;
        }

        // ------------------------------------------------------------------
        // Address classification, for feedback that explains itself
        // ------------------------------------------------------------------

        public bool IsLoopback => (Value & 0xFF000000u) == 0x7F000000u;             // 127/8

        public bool IsMulticastOrReserved => (Value & 0xF0000000u) >= 0xE0000000u;  // 224/4 and up

        /// <summary>169.254/16 - what Windows assigns itself when DHCP fails.
        /// Worth naming, because a learner seeing one in ipconfig has found a
        /// real symptom, not a typo.</summary>
        public bool IsLinkLocal => (Value & 0xFFFF0000u) == 0xA9FE0000u;

        /// <summary>RFC 1918: 10/8, 172.16/12, 192.168/16.</summary>
        public bool IsPrivate
        {
            get
            {
                if ((Value & 0xFF000000u) == 0x0A000000u) return true;
                if ((Value & 0xFFF00000u) == 0xAC100000u) return true;
                if ((Value & 0xFFFF0000u) == 0xC0A80000u) return true;
                return false;
            }
        }

        // ------------------------------------------------------------------

        public bool Equals(IPv4Address other) => Value == other.Value;
        public override bool Equals(object obj) => obj is IPv4Address other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();

        public static bool operator ==(IPv4Address a, IPv4Address b) => a.Value == b.Value;
        public static bool operator !=(IPv4Address a, IPv4Address b) => a.Value != b.Value;
    }
}
