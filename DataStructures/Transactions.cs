using System;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Core;

namespace atomex_frontend.atomex_data_structures
{
  public class Transaction : IBlockchainTransaction
  {
    public Transaction(
      string id,
      BlockchainTransactionState state,
      BlockchainTransactionType type,
      DateTime? creationTime,
      bool isConfirmed,
      double amount)
    {
      Id = id;
      State = state;
      Type = type;
      CreationTime = creationTime;
      IsConfirmed = isConfirmed;
      Amount = amount;
    }

    public string Id { get; set; }
    public BlockchainTransactionState State { get; set; }
    public BlockchainTransactionType Type { get; set; }
    public DateTime? CreationTime { get; set; }
    public bool IsConfirmed { get; set; }

    public double Amount { get; set; }

    public Currency Currency => throw new NotImplementedException();
    public BlockInfo BlockInfo => throw new NotImplementedException();
  }
}