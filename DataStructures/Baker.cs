using Atomex.Blockchain.Tezos;
using System;

namespace atomex_frontend.atomex_data_structures
{
  public class Baker : BakerData
  {
    public bool IsFull => StakingAvailable <= 0;
    public bool IsMinDelegation => MinDelegation > 0;
  }

  public class Delegation
  {
    public BakerData Baker { get; set; }
    public string Address { get; set; }
    public decimal Balance { get; set; }

    public string BbUri { get; set; }
    public DateTime DelegationTime { get; set; }
    public string Status { get; set; }

  }
}