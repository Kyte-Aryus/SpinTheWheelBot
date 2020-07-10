using System;
using System.Collections.Generic;
using System.Text;

namespace SpinTheWheel.Utilities
{
    public class Prize
    {
        public enum PrizeType
        {
            // Leaving room for expansion later
            ROLE,
            UNKNOWN
        }

        public string Name { get; set; }
        public string Description { get; set; }
        public PrizeType Type { get; set; }
        public ulong RoleId { get; set; } // Required if PrizeType == ROLE
        public int RoleTime { get; set; } // Optional. Only appliciable if PrizeType == ROLE
        public int RoleTimeVariation { get; set; } // Optional. Only appliciable if PrizeType == ROLE
        public bool IsSilencingRole { get; set; } // Optional. Only appliciable if PrizeType == ROLE
        public bool MoveUserBackAfterSilence { get; set; } // Optional. Only appliciable if PrizeType == ROLE
        public string ImageResourcePath { get; set; } // Optional
        public string Message { get; set; }
        public uint Odds { get; set; }
    }
}
