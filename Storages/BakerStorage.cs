using System;
using System.Linq;
using System.Globalization;
using Atomex.Wallet;
using System.Threading;
using System.Threading.Tasks;
using Atomex;
using Atomex.MarketData.Abstract;
using Atomex.Core;
using System.Collections.Generic;

using atomex_frontend.atomex_data_structures;
using atomex_frontend.Common;
using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Tezos.Internal;
using Atomex.Common;
using Newtonsoft.Json.Linq;

namespace atomex_frontend.Storages
{
  public class BakerStorage
  {
    public BakerStorage(AccountStorage AS, Toolbelt.Blazor.I18nText.I18nText I18nText)
    {
      this.accountStorage = AS;
      LoadTranslations(I18nText);

    }

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
      UseDefaultFee = true;
      Warning = null;

      PrepareWallet().WaitForResult();
      LoadBakerList().FireAndForget();
    }

    public event Action RefreshUI;
    private void CallRefreshUI()
    {
      RefreshUI?.Invoke();
    }

    I18nText.Translations Translations = new I18nText.Translations();

    private AccountStorage accountStorage;
    private IAtomexApp App { get => accountStorage.AtomexApp; }

    private Tezos _tezos;
    private WalletAddress _walletAddress;
    private TezosTransaction _tx;

    public WalletAddress WalletAddress
    {
      get => _walletAddress;
      set
      {
        _walletAddress = value;
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

    private Baker _baker;
    public Baker Baker
    {
      get => _baker;
      set
      {
        _baker = value;

        if (_baker != null)
          Address = _baker.Address;
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
        _fee = value;

        if (!UseDefaultFee)
        {
          var feeAmount = _fee;

          if (feeAmount > _walletAddress.Balance)
            feeAmount = _walletAddress.Balance;

          _fee = feeAmount;

          Warning = string.Empty;
        }
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

    private bool _useDefaultFee = true;
    public bool UseDefaultFee
    {
      get => _useDefaultFee;
      set
      {
        _useDefaultFee = value;
        NextCommand().FireAndForget();
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
          Baker = null;
        else if (baker != Baker)
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


    public async Task NextCommand()
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

        if (Fee < 0)
        {
          Warning = Translations.SvCommissionLessThanZeroError;
          return;
        }

        var result = await GetDelegate();

        if (result.HasError)
          Warning = result.Error.Description;
        else
        {
          //todo: Load confirmation page;
        }
      }
      finally
      {
        DelegationCheck = false;
        CallRefreshUI();
      }
    }

    private readonly Action _onDelegate;

    private async Task LoadBakerList()
    {
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
          Address = "tz1hoJ3GvL8Wo9NerKFnLQivf9SSngDyhhrg",
          Logo = "https://services.tzkt.io/v1/avatars/tz1hoJ3GvL8Wo9NerKFnLQivf9SSngDyhhrg",
          Name = "Test baker #1",
          Fee = 0.03m,
          MinDelegation = 100,
          StakingAvailable = 4000
        });

        bakers.Add(new Baker
        {
          Address = "tz1aWXP237BLwNHJcCD4b3DutCevhqq2T1Z9",
          Logo = "https://services.tzkt.io/v1/avatars/tz1hoJ3GvL8Wo9NerKFnLQivf9SSngDyhhrg",
          Name = "Test baker #2",
          Fee = 0.07m,
          MinDelegation = 500,
          StakingAvailable = 8000
        });
      }

      FromBakersList = bakers;

      await NextCommand();
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
        Warning = "You don't have non-empty accounts";
        return;
      }

      WalletAddress = FromAddressList.FirstOrDefault();
    }

    private async Task<Result<string>> GetDelegate(
        CancellationToken cancellationToken = default)
    {
      if (_walletAddress == null)
        return new Error(Errors.InvalidWallets, "You don't have non-empty accounts");

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
        return new Error(Errors.WrongDelegationAddress, "Wrong delegation address");
      }

      if (delegateData["deactivated"].Value<bool>())
        return new Error(Errors.WrongDelegationAddress, "Baker is deactivated. Pick another one");

      var delegators = delegateData["delegated_contracts"]?.Values<string>();

      if (delegators.Contains(_walletAddress.Address))
        return new Error(Errors.AlreadyDelegated, $"Already delegated from {_walletAddress.Address} to {_address}");

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
          return new Error(Errors.TransactionCreationError, $"Autofill transaction failed");

        Fee = tx.Fee;
        _tx = tx;
      }
      catch (Exception e)
      {
        Console.WriteLine("Autofill delegation error");
        return new Error(Errors.TransactionCreationError, $"Autofill delegation error. Try again later");
      }

      return "Successful check";
    }
  }
}