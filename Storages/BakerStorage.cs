using System;
using System.Linq;
using System.Globalization;
using Atomex.Wallet;
using System.Threading;
using System.Threading.Tasks;
using Atomex;
using Atomex.Blockchain.Tezos;
using Atomex.Core;
using System.Collections.Generic;

using atomex_frontend.atomex_data_structures;
using atomex_frontend.Common;
using Atomex.Blockchain.Tezos.Internal;
using Atomex.Common;
using Newtonsoft.Json.Linq;
using Serilog;

namespace atomex_frontend.Storages
{
  public class BakerStorage
  {
    public BakerStorage(AccountStorage AS, Toolbelt.Blazor.I18nText.I18nText I18nText)
    {
      this.accountStorage = AS;
      LoadTranslations(I18nText);
    }

    I18nText.Translations Translations = new I18nText.Translations();
    private async void LoadTranslations(Toolbelt.Blazor.I18nText.I18nText I18nText)
    {
      Translations = await I18nText.GetTextTableAsync<I18nText.Translations>(null);
    }

    public void Initialize()
    {
      _tezos = App.Account.Currencies.Get<Tezos>("XTZ");
      FeeCurrencyCode = _tezos.FeeCode;
      BaseCurrencyCode = "USD";
      BaseCurrencyFormat = "$0.00";
      Warning = null;
      _useDefaultFee = false;
      _fee = 0m;

      PrepareWallet().WaitForResult();
      LoadBakerList(firstLoad: true).FireAndForget();
    }

    public event Action RefreshUI;
    private void CallRefreshUI()
    {
      RefreshUI?.Invoke();
    }

    private AccountStorage accountStorage;
    private IAtomexApp App { get => accountStorage.AtomexApp; }

    public Tezos _tezos;
    public string CUSTOM_BAKER_NAME = "Custom baker";
    private WalletAddress _walletAddress;
    private TezosTransaction _tx;

    public WalletAddress WalletAddress
    {
      get => _walletAddress;
      set
      {
        _walletAddress = value;
        if (!UseDefaultFee)
        {
          _fee = 0m;
        }
      }
    }

    private List<Baker> _fromBakersList;
    public List<Baker> FromBakersList
    {
      get => _fromBakersList;
      private set
      {
        _fromBakersList = value;

        Baker = FromBakersList.FirstOrDefault();
      }
    }

    private List<WalletAddress> _fromAddressList;
    public List<WalletAddress> FromAddressList
    {
      get => _fromAddressList;
      private set
      {
        _fromAddressList = value;

        WalletAddress = FromAddressList.FirstOrDefault();
      }
    }

    public void SetWalletAddress(string Address)
    {
      WalletAddress = FromAddressList.Find(wa => wa.Address == Address);
    }

    private Baker _baker;
    public Baker Baker
    {
      get => _baker;
      set
      {
        var oldBakerName = _baker?.Name;
        _baker = value;

        if (_baker.Name != CUSTOM_BAKER_NAME)
        {
          Address = _baker.Address;
        }
        else if (oldBakerName != CUSTOM_BAKER_NAME)
        {
          _address = "";
        }

        CallRefreshUI();
      }
    }

    public string FeeString
    {
      get => Fee.ToString(_tezos.FeeFormat, CultureInfo.InvariantCulture);
      set
      {
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var fee))
          return;

        Fee = Helper.TruncateByFormat(fee, _tezos.FeeFormat);
      }
    }

    private decimal _fee;
    public decimal Fee
    {
      get => _fee;
      set
      {
        if (value != _fee)
        {
          _fee = value;

          if (!UseDefaultFee)
          {
            var feeAmount = _fee;

            if (feeAmount > _walletAddress.Balance)
              feeAmount = _walletAddress.Balance;

            _fee = feeAmount;

            Warning = string.Empty;

            CheckFee().FireAndForget();
          }
        }
      }
    }

    private bool _feeChecking = false;
    public bool FeeChecking
    {
      get => _feeChecking;
      set
      {
        _feeChecking = value;
        CallRefreshUI();
      }
    }

    private string _baseCurrencyFormat;
    public string BaseCurrencyFormat
    {
      get => _baseCurrencyFormat;
      set { _baseCurrencyFormat = value; }
    }

    private decimal _feeInBase;
    public decimal FeeInBase
    {
      get => _feeInBase;
      set { _feeInBase = value; }
    }

    private string _feeCurrencyCode;
    public string FeeCurrencyCode
    {
      get => _feeCurrencyCode;
      set { _feeCurrencyCode = value; }
    }

    private string _baseCurrencyCode;
    public string BaseCurrencyCode
    {
      get => _baseCurrencyCode;
      set { _baseCurrencyCode = value; }
    }

    private bool _useDefaultFee = false;
    public bool UseDefaultFee
    {
      get => _useDefaultFee;
      set
      {
        _useDefaultFee = value;
        if (_useDefaultFee)
        {
          CheckFee().FireAndForget();
        }
      }
    }

    private string _address;
    public string Address
    {
      get => _address;
      set
      {
        _address = value;

        var baker = FromBakersList.FirstOrDefault(b => b.Address == _address);

        if (baker == null)
          Baker = new Baker
          {
            Name = CUSTOM_BAKER_NAME,
            Address = "",
          };
        else if (baker.Name != Baker.Name)
          Baker = baker;
      }
    }

    private string _warning;
    public string Warning
    {
      get => _warning;
      set { _warning = value; CallRefreshUI(); }
    }

    private bool _delegationCheck;
    public bool DelegationCheck
    {
      get => _delegationCheck;
      set { _delegationCheck = value; }
    }

    private bool _canDelegate;
    public bool CanDelegate
    {
      get => _canDelegate;
      set { _canDelegate = value; }
    }

    private bool _hasDelegations;
    public bool HasDelegations
    {
      get => _hasDelegations;
      set { _hasDelegations = value; }
    }

    private List<Delegation> _delegations = new List<Delegation>();
    public List<Delegation> Delegations
    {
      get => _delegations;
      set { _delegations = value; }
    }


    public bool GetAddressDelegated(string address)
    {
      return Delegations.FindIndex(del => del.Address == address) != -1;
    }

    public BakerData GetBakerDataByAddress(string address)
    {
      return Delegations.Find(del => del.Address == address).Baker;
    }

    public async Task NextCommand(bool firstLoad = false)
    {
      if (DelegationCheck)
        return;

      DelegationCheck = true;

      try
      {
        if (string.IsNullOrEmpty(Address))
        {
          Warning = Translations.SvEmptyAddressError;
          return;
        }

        if (!_tezos.IsValidAddress(Address))
        {
          Warning = Translations.SvInvalidAddressError;
          return;
        }

        if (Fee <= 0)
        {
          Warning = Translations.SvCommissionLessThanZeroError;
          return;
        }

        var result = await GetDelegate(firstLoad);

        if (result.HasError)
          Warning = result.Error.Description;
      }
      finally
      {
        DelegationCheck = false;
        CallRefreshUI();
      }
    }

    private readonly Action _onDelegate;

    private async Task LoadBakerList(bool firstLoad = false)
    {
      Console.WriteLine("Loading bakers");
      List<Baker> bakers = null;

      try
      {
        await Task.Run(async () =>
        {
          bakers = (await BbApi
                      .GetBakers(App.Account.Network)
                      .ConfigureAwait(false))
                      .Select(x => new Baker
                      {
                        Address = x.Address,
                        Logo = x.Logo,
                        Name = x.Name,
                        Fee = x.Fee,
                        EstimatedRoi = x.EstimatedRoi,
                        MinDelegation = x.MinDelegation,
                        StakingAvailable = x.StakingAvailable
                      })
                      .ToList();
        });
      }
      catch (Exception e)
      {
        Console.WriteLine(e.Message, "Error while fetching bakers list");
      }

      Console.WriteLine(bakers.Count);
      if (App.Account.Network == Network.TestNet && bakers.Count == 0)
      {
        bakers.Add(new Baker
        {
          Address = "tz1VxS7ff4YnZRs8b4mMP4WaMVpoQjuo1rjf",
          Logo = "https://services.tzkt.io/v1/avatars/tz1hoJ3GvL8Wo9NerKFnLQivf9SSngDyhhrg",
          Name = "Test baker #1",
          Fee = 0.03m,
          EstimatedRoi = 0.04m,
          MinDelegation = 100,
          StakingAvailable = 4000
        });

        bakers.Add(new Baker
        {
          Address = "tz1PirboZKFVqkfE45hVLpkpXaZtLk3mqC17",
          Logo = "https://services.tzkt.io/v1/avatars/tz1hoJ3GvL8Wo9NerKFnLQivf9SSngDyhhrg",
          Name = "Test baker #2",
          Fee = 0.07m,
          EstimatedRoi = 0.05m,
          MinDelegation = 500,
          StakingAvailable = 8000
        });
      }

      bakers.Insert(0, new Baker
      {
        Name = CUSTOM_BAKER_NAME,
        Address = "",
      });

      FromBakersList = bakers;

      //await NextCommand(firstLoad);
      CallRefreshUI();
    }

    private async Task PrepareWallet(CancellationToken cancellationToken = default)
    {
      FromAddressList = (await App.Account
          .GetUnspentAddressesAsync(_tezos.Name, cancellationToken).ConfigureAwait(false))
          .OrderByDescending(x => x.Balance)
          .ToList();

      if (!FromAddressList?.Any() ?? false)
      {
        Warning = Translations.NoEmptyAccounts;
        return;
      }

      WalletAddress = FromAddressList.FirstOrDefault();
    }


    private async Task<Result<string>> CheckFee()
    {
      FeeChecking = true;
      try
      {
        if (string.IsNullOrEmpty(Address))
        {
          var err = new Error(Errors.WrongDelegationAddress, Translations.SvEmptyAddressError);
          Warning = err.Description;
          return err;
        }

        if (!_tezos.IsValidAddress(Address))
        {
          var err = new Error(Errors.WrongDelegationAddress, Translations.SvInvalidAddressError);
          Warning = err.Description;
          return err;
        }

        var wallet = (HdWallet)App.Account.Wallet;
        var keyStorage = wallet.KeyStorage;

        var rpc = new Rpc(_tezos.RpcNodeUri);

        JObject delegateData;
        try
        {
          delegateData = await rpc
              .GetDelegate(_address)
              .ConfigureAwait(false);
        }
        catch
        {
          var err = new Error(Errors.WrongDelegationAddress, Translations.WrongDelegationAddress);
          Warning = err.Description;
          return err;
        }

        if (delegateData["deactivated"].Value<bool>())
        {
          var err = new Error(Errors.WrongDelegationAddress, $"{Translations.BakerDeactivated}");
          Warning = err.Description;
          return err;
        }

        var delegators = delegateData["delegated_contracts"]?.Values<string>();

        if (delegators.Contains(_walletAddress.Address))
        {
          var err = new Error(Errors.AlreadyDelegated, $"{Translations.AlreadyDelagated} {Translations.From.ToLower()} {_walletAddress.Address} {Translations.To.ToLower()} {_address}");
          Warning = err.Description;
          return err;
        }

        var tx = new TezosTransaction
        {
          StorageLimit = _tezos.StorageLimit,
          GasLimit = _tezos.GasLimit,
          From = _walletAddress.Address,
          To = _address,
          Fee = Fee.ToMicroTez(),
          Currency = _tezos,
          CreationTime = DateTime.UtcNow,
        };

        var calculatedFee = await tx.AutoFillAsync(keyStorage, _walletAddress, true);
        if (!calculatedFee)
        {
          var err = new Error(Errors.TransactionCreationError, Translations.AutofillTxFailed);
          Warning = err.Description;
          return err;
        }

        if (UseDefaultFee)
        {
          Fee = tx.Fee;
        }
        else
        {
          if (Fee < tx.Fee)
          {
            Fee = tx.Fee;
          }
        }
      }
      catch (Exception)
      {
        Console.WriteLine(Translations.WrongDelegationAddress);
        var err = new Error(Errors.TransactionCreationError, Translations.WrongDelegationAddress);
        Warning = err.Description;
        return err;
      }
      finally
      {
        FeeChecking = false;
      }
      return "Successful check";
    }

    private async Task<Result<string>> GetDelegate(
        bool firstLoad,
        CancellationToken cancellationToken = default)
    {
      if (_walletAddress == null)
        return new Error(Errors.InvalidWallets, Translations.NoEmptyAccounts);

      var wallet = (HdWallet)App.Account.Wallet;
      var keyStorage = wallet.KeyStorage;
      var rpc = new Rpc(_tezos.RpcNodeUri);

      JObject delegateData;
      try
      {
        delegateData = await rpc
            .GetDelegate(_address)
            .ConfigureAwait(false);
      }
      catch
      {
        return new Error(Errors.WrongDelegationAddress, Translations.WrongDelegationAddress);
      }

      if (delegateData["deactivated"].Value<bool>())
        return new Error(Errors.WrongDelegationAddress, $"{Translations.BakerDeactivated}");

      var delegators = delegateData["delegated_contracts"]?.Values<string>();

      if (delegators.Contains(_walletAddress.Address))
      {
        if (!firstLoad)
        {
          return new Error(Errors.AlreadyDelegated, $"{Translations.AlreadyDelagated} {Translations.From.ToLower()} {_walletAddress.Address} {Translations.To.ToLower()} {_address}");
        }
        else
        {
          return null;
        }
      }

      var tx = new TezosTransaction
      {
        StorageLimit = _tezos.StorageLimit,
        GasLimit = _tezos.GasLimit,
        From = _walletAddress.Address,
        To = _address,
        Fee = Fee.ToMicroTez(),
        Currency = _tezos,
        CreationTime = DateTime.UtcNow,
      };

      try
      {
        var calculatedFee = await tx.AutoFillAsync(keyStorage, _walletAddress, UseDefaultFee);
        if (!calculatedFee)
          return new Error(Errors.TransactionCreationError, Translations.AutofillTxFailed);

        Fee = tx.Fee;
        _tx = tx;
      }
      catch (Exception e)
      {
        Console.WriteLine(Translations.AutofillDelegationError);
        return new Error(Errors.TransactionCreationError, Translations.AutofillDelegationError);
      }

      return "Successful check";
    }


    public async Task<Result<string>> Send()
    {
      var wallet = (HdWallet)App.Account.Wallet;
      var keyStorage = wallet.KeyStorage;
      var tezos = _tezos;

      try
      {
        var signResult = await _tx
            .SignDelegationOperationAsync(keyStorage, WalletAddress, default);

        if (!signResult)
        {
          Log.Error(Translations.TxSigningError);
          Warning = (Translations.TxSigningError);
          return String.Empty;
        }

        var result = await tezos.BlockchainApi
            .TryBroadcastAsync(_tx);

        if (result.Error != null)
        {
          Warning = result.Error?.Description;
          return String.Empty;
        }
        else
        {
          return result;
        }

      }
      catch (Exception e)
      {
        Warning = Translations.DelegationError;
        Log.Error(e, Translations.DelegationError);
      }
      return string.Empty;
    }

    public async Task LoadDelegationInfoAsync()
    {
      try
      {
        if (_tezos == null)
        {
          try
          {
            _tezos = App.Account.Currencies.Get<Tezos>("XTZ");
          }
          catch
          {
            Log.Error(Translations.CantGetTezos);
            return;
          }
        }

        var tezos = _tezos;

        var balance = await App.Account
            .GetBalanceAsync(tezos.Name)
            .ConfigureAwait(false);

        var addresses = await App.Account
            .GetUnspentAddressesAsync(tezos.Name)
            .ConfigureAwait(false);

        var rpc = new Rpc(tezos.RpcNodeUri);

        foreach (var wa in addresses.ToList())
        {
          var accountData = await rpc
              .GetAccount(wa.Address)
              .ConfigureAwait(false);

          var @delegate = accountData["delegate"]?.ToString();

          if (string.IsNullOrEmpty(@delegate))
            continue;


          var baker = await BbApi
              .GetBaker(@delegate, App.Account.Network)
              .ConfigureAwait(false);

          var delegationIndex = Delegations.FindIndex(d => d.Address == wa.Address);

          var delegation = new Delegation
          {
            Baker = baker,
            Address = wa.Address,
            Balance = wa.Balance
          };

          if (delegationIndex == -1)
          {
            Delegations.Add(delegation);
          }
          else
          {
            Delegations[delegationIndex] = delegation;
          }
        }
      }
      catch (Exception e)
      {
        Log.Error(e, "LoadDelegationInfoAsync error");
      }
      finally
      {
        CallRefreshUI();
      }
    }

  }
}