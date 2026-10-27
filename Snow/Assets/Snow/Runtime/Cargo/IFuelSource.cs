using UnityEngine;

namespace Snow.Cargo
{
    /// <summary>
    /// A carried fuel container another system can pour from (the van's tank, user 27.10): the jerrycan
    /// (Snow.Levels.Forest.FuelCan) lives in the Levels assembly, the van in Runtime, so they meet here.
    /// </summary>
    public interface IFuelSource
    {
        /// <summary>Litres in it now (replicated).</summary>
        float FuelL { get; }

        /// <summary>Server: add (or take, negative) litres; returns what was actually moved.</summary>
        float ServerAdd(float litres);

        /// <summary>The can's middle, for the reach to a filler neck.</summary>
        Vector3 Center { get; }
    }
}
