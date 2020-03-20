using System;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Core;

namespace atomex_frontend.atomex_data_structures
{
  public class Transaction : IBlockchainTransaction
  {
    public Transaction(
      Currency currency,
      string id,
      BlockchainTransactionState state,
      BlockchainTransactionType type,
      DateTime? creationTime,
      bool isConfirmed,
      decimal amount)
    {
      Currency = currency;
      Id = id;
      State = state;
      Type = type;
      CreationTime = creationTime;
      IsConfirmed = isConfirmed;
      Amount = amount;
    }

    public Currency Currency { get; set; }
    public string Id { get; set; }
    public BlockchainTransactionState State { get; set; }
    public BlockchainTransactionType Type { get; set; }
    public DateTime? CreationTime { get; set; }
    public bool IsConfirmed { get; set; }

    public decimal Amount { get; set; }
    public BlockInfo BlockInfo => throw new NotImplementedException();
  }
}