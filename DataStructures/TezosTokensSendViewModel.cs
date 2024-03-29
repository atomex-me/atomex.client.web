using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using Atomex;
using atomex_frontend.I18nText;
using atomex_frontend.Storages;
using Atomex.Core;
using Atomex.Common;
using Atomex.MarketData.Abstract;
using Atomex.Wallet.Tezos;
using Atomex.Wallet.Abstract;
using Atomex.TezosTokens;

namespace atomex_frontend.atomex_data_structures
{
    public class TezosTokensSendViewModel 
    {
        public const string DefaultCurrencyFormat = "F8";
        public const string DefaultBaseCurrencyCode = "USD";
        public const string DefaultBaseCurrencyFormat = "$0.00";
        public const int MaxCurrencyDecimals = 9;

        private readonly IAtomexApp _app;

        public ObservableCollection<TezosTokenViewModel> Tokens { get; set; }
        public TezosTokenViewModel Token { get; set; }

        private ObservableCollection<WalletAddressViewModel> _fromAddresses;
        public ObservableCollection<WalletAddressViewModel> FromAddresses
        {
            get => _fromAddresses;
            set
            {
                _fromAddresses = value;
            }
        }

        private string _from;
        public string From
        {
            get => _from;
            set
            {
                _from = value;

                Warning = string.Empty;
                Amount = _amount;
                Fee = _fee;

                UpdateCurrencyCode();
            }
        }

        public string FromBalance { get; set; }

        private string _tokenContract;
        public string TokenContract
        {
            get => _tokenContract;
            set
            {
                _tokenContract = value;
                
                Warning = string.Empty;
                Amount = _amount;
                Fee = _fee;

                UpdateFromAddressList(_from, _tokenContract, _tokenId);
                UpdateCurrencyCode();
            }
        }

        private decimal _tokenId;
        public decimal TokenId
        {
            get => _tokenId;
            set
            {
                _tokenId = value;

                Warning = string.Empty;
                Amount = _amount;
                Fee = _fee;

                UpdateFromAddressList(_from, _tokenContract, _tokenId);
                UpdateCurrencyCode();
            }
        }

        protected string _to;
        public virtual string To
        {
            get => _to;
            set
            {
                _to = value;
                // OnPropertyChanged(nameof(To));

                Warning = string.Empty;
            }
        }

        public string CurrencyFormat { get; set; }
        public string FeeCurrencyFormat { get; set; }

        private string _baseCurrencyFormat;
        public virtual string BaseCurrencyFormat
        {
            get => _baseCurrencyFormat;
            set { _baseCurrencyFormat = value; }
        }

        protected decimal _amount;
        public decimal Amount
        {
            get => _amount;
            set { UpdateAmount(value); }
        }

        private bool _isAmountUpdating;
        public bool IsAmountUpdating
        {
            get => _isAmountUpdating;
            set { _isAmountUpdating = value; }
        }

        public string AmountString
        {
            get => Amount.ToString(CurrencyFormat, CultureInfo.InvariantCulture);
            set
            {
                if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
                    return;

                Amount = amount.TruncateByFormat(CurrencyFormat);
            }
        }

        private bool _isFeeUpdating;
        public bool IsFeeUpdating
        {
            get => _isFeeUpdating;
            set { _isFeeUpdating = value; }
        }

        protected decimal _fee;
        public decimal Fee
        {
            get => _fee;
            set { UpdateFee(value); }
        }

        public virtual string FeeString
        {
            get => Fee.ToString(FeeCurrencyFormat, CultureInfo.InvariantCulture);
            set
            {
                if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var fee))
                    return;

                Fee = fee.TruncateByFormat(FeeCurrencyFormat);
            }
        }

        protected bool _useDefaultFee;
        public virtual bool UseDefaultFee
        {
            get => _useDefaultFee;
            set
            {
                _useDefaultFee = value;
                
                if (_useDefaultFee)
                    Fee = _fee;
            }
        }

        protected decimal _amountInBase;
        public decimal AmountInBase
        {
            get => _amountInBase;
            set { _amountInBase = value; }
        }

        protected decimal _feeInBase;
        public decimal FeeInBase
        {
            get => _feeInBase;
            set { _feeInBase = value; }
        }

        protected string _currencyCode;
        public string CurrencyCode
        {
            get => _currencyCode;
            set { _currencyCode = value; }
        }

        protected string _feeCurrencyCode;
        public string FeeCurrencyCode
        {
            get => _feeCurrencyCode;
            set { _feeCurrencyCode = value; }
        }

        protected string _baseCurrencyCode;
        public string BaseCurrencyCode
        {
            get => _baseCurrencyCode;
            set { _baseCurrencyCode = value; }
        }

        protected string _warning;
        public string Warning
        {
            get => _warning;
            set
            {
                _warning = value;
                CallUIRefresh();
            }
        }

        public SendConfirmationViewModel SendConfirmationViewModel { get; set; }

        Translations Translations = new Translations();
        
        public event Action UIRefresh;
        private void CallUIRefresh()
        {
            UIRefresh?.Invoke();
        }

        public TezosTokensSendViewModel(
            Translations translations,
            IAtomexApp app,
            string from = null,
            string tokenContract = null,
            decimal tokenId = 0)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            Translations = translations;

            var tezosConfig = _app.Account
                .Currencies
                .Get<TezosConfig>(TezosConfig.Xtz);

            CurrencyCode     = "";
            FeeCurrencyCode  = TezosConfig.Xtz;
            BaseCurrencyCode = DefaultBaseCurrencyCode;

            CurrencyFormat     = DefaultCurrencyFormat;
            FeeCurrencyFormat  = tezosConfig.FeeFormat;
            BaseCurrencyFormat = DefaultBaseCurrencyFormat;

            _tokenContract = tokenContract;
            _tokenId       = tokenId;

            UpdateFromAddressList(from, tokenContract, tokenId);
            UpdateCurrencyCode();

            SubscribeToServices();

            UseDefaultFee = true;
        }

        private void SubscribeToServices()
        {
            if (_app.HasQuotesProvider)
                _app.QuotesProvider.QuotesUpdated += OnQuotesUpdatedEventHandler;
        }

        public async virtual Task OnNextCommand()
        {
            var tezosConfig = _app.Account
                .Currencies
                .Get<TezosConfig>(TezosConfig.Xtz);

            if (string.IsNullOrEmpty(To))
            {
                Warning = Translations.SvEmptyAddressError;
                return;
            }

            if (!tezosConfig.IsValidAddress(To))
            {
                Warning = Translations.SvInvalidAddressError;
                return;
            }

            if (Amount <= 0)
            {
                Warning = Translations.SvAmountLessThanZeroError;
                return;
            }

            if (Fee <= 0)
            {
                Warning = Translations.SvCommissionLessThanZeroError;
                return;
            }

            if (TokenContract == null || From == null)
            {
                Warning = "Invalid 'From' address or token contract address!";
                return;
            }

            if (!tezosConfig.IsValidAddress(TokenContract))
            {
                Warning = "Invalid token contract address!";
                return;
            }

            var fromTokenAddress = await GetTokenAddressAsync(_app.Account, From, TokenContract, TokenId);

            if (fromTokenAddress == null)
            {
                Warning = $"Insufficient token funds on address {From}! Please update your balance!";
                return;
            }

            if (_amount > fromTokenAddress.Balance)
            {
                Warning = $"Insufficient token funds on address {fromTokenAddress.Address}! Please use Max button to find out how many tokens you can send!";
                return;
            }

            var xtzAddress = await _app.Account
                .GetAddressAsync(TezosConfig.Xtz, From);

            if (xtzAddress == null)
            {
                Warning = $"Insufficient funds for fee. Please update your balance for address {From}!";
                return;
            }

            if (xtzAddress.AvailableBalance() < _fee)
            {
                Warning = $"Insufficient funds for fee!";
                return;
            }
            
            Console.WriteLine($"Creating SendConfirmationViewModel");

            SendConfirmationViewModel = new SendConfirmationViewModel()
            {
                App                = _app,
                Currency           = tezosConfig,
                From               = From,
                To                 = To,
                Amount             = Amount,
                AmountInBase       = AmountInBase,
                BaseCurrencyCode   = BaseCurrencyCode,
                BaseCurrencyFormat = BaseCurrencyFormat,
                Fee                = Fee,
                UseDeafultFee      = UseDefaultFee,
                FeeInBase          = FeeInBase,
                CurrencyCode       = CurrencyCode,
                CurrencyFormat     = CurrencyFormat,
            
                FeeCurrencyCode    = FeeCurrencyCode,
                FeeCurrencyFormat  = FeeCurrencyFormat,
            
                TokenContract      = TokenContract,
                TokenId            = TokenId
            };
        }

        protected virtual async void UpdateAmount(decimal amount)
        {
            if (IsAmountUpdating)
                return;

            IsAmountUpdating = true;
            Warning = string.Empty;

            _amount = amount;
            CallUIRefresh();
            
            try
            {
                var tezosConfig = _app.Account
                    .Currencies
                    .Get<TezosConfig>(TezosConfig.Xtz);

                if (TokenContract == null || From == null)
                {
                    Warning = "Invalid 'From' address or token contract address!";
                    return;
                }

                if (!tezosConfig.IsValidAddress(TokenContract))
                {
                    Warning = "Invalid token contract address!";
                    return;
                }

                var fromTokenAddress = await GetTokenAddressAsync(_app.Account, From, TokenContract, TokenId);

                if (fromTokenAddress == null)
                {
                    Warning = $"Insufficient token funds on address {From}! Please update your balance!";
                    return;
                }

                if (_amount > fromTokenAddress.Balance)
                {
                    Warning = $"Insufficient token funds on address {fromTokenAddress.Address}! Please use Max button to find out how many tokens you can send!";
                    return;
                }

                OnQuotesUpdatedEventHandler(_app.QuotesProvider, EventArgs.Empty);
            }
            finally
            {
                IsAmountUpdating = false;
            }
        }

        protected virtual async void UpdateFee(decimal fee)
        {
            if (IsFeeUpdating)
                return;

            IsFeeUpdating = true;

            try
            {
                var tezosConfig = _app.Account
                    .Currencies
                    .Get<TezosConfig>(TezosConfig.Xtz);

                if (TokenContract == null || From == null)
                {
                    Warning = "Invalid 'From' address or token contract address!";
                    return;
                }

                if (!tezosConfig.IsValidAddress(TokenContract))
                {
                    Warning = "Invalid token contract address!";
                    return;
                }

                if (UseDefaultFee)
                {
                    var fromTokenAddress = await GetTokenAddressAsync(_app.Account, From, TokenContract, TokenId);

                    if (fromTokenAddress == null)
                    {
                        Warning = $"Insufficient token funds on address {From}! Please update your balance!";
                        return;
                    }

                    var tokenAccount = _app.Account
                        .GetTezosTokenAccount<TezosTokenAccount>(fromTokenAddress.Currency, TokenContract, TokenId);

                    var (estimatedFee, isEnougth) = await tokenAccount
                        .EstimateTransferFeeAsync(From);

                    if (!isEnougth)
                    {
                        Warning = $"Insufficient funds for fee. Minimum {estimatedFee} XTZ is required!";
                        return;
                    }

                    _fee = estimatedFee;
                }
                else
                {
                    var xtzAddress = await _app.Account
                        .GetAddressAsync(TezosConfig.Xtz, From);

                    if (xtzAddress == null)
                    {
                        Warning = $"Insufficient funds for fee. Please update your balance for address {From}!";
                        return;
                    }

                    _fee = Math.Min(fee, tezosConfig.GetMaximumFee());

                    if (xtzAddress.AvailableBalance() < _fee)
                    {
                        Warning = $"Insufficient funds for fee!";
                        return;
                    }
                }
                
                OnQuotesUpdatedEventHandler(_app.QuotesProvider, EventArgs.Empty);
            }
            finally
            {
                IsFeeUpdating = false;
            }
        }

        public virtual async void OnMaxClick()
        {
            if (IsAmountUpdating)
                return;

            IsAmountUpdating = true;

            Warning = string.Empty;

            try
            {
                var tezosConfig = _app.Account
                    .Currencies
                    .Get<TezosConfig>(TezosConfig.Xtz);

                if (TokenContract == null || From == null)
                {
                    _amount = 0;

                    return;
                }

                if (!tezosConfig.IsValidAddress(TokenContract))
                {
                    _amount = 0;

                    Warning = "Invalid token contract address!";
                    return;
                }

                var fromTokenAddress = await GetTokenAddressAsync(_app.Account, From, TokenContract, TokenId);

                if (fromTokenAddress == null)
                {
                    _amount = 0;

                    Warning = $"Insufficient token funds on address {From}! Please update your balance!";
                    return;
                }

                _amount = fromTokenAddress.Balance;

                OnQuotesUpdatedEventHandler(_app.QuotesProvider, EventArgs.Empty);
            }
            finally
            {
                IsAmountUpdating = false;
            }
        }

        protected virtual void OnQuotesUpdatedEventHandler(object sender, EventArgs args)
        {
            if (!(sender is ICurrencyQuotesProvider quotesProvider))
                return;

            AmountInBase = !string.IsNullOrEmpty(CurrencyCode)
                ? Amount * (quotesProvider.GetQuote(CurrencyCode, BaseCurrencyCode)?.Bid ?? 0m)
                : 0;

            FeeInBase = !string.IsNullOrEmpty(FeeCurrencyCode)
                ? Fee * (quotesProvider.GetQuote(FeeCurrencyCode, BaseCurrencyCode)?.Bid ?? 0m)
                : 0;
            
            CallUIRefresh();
        }

        public static async Task<WalletAddress> GetTokenAddressAsync(
            IAccount account,
            string address,
            string tokenContract,
            decimal tokenId)
        {
            var tezosAccount = account
                .GetCurrencyAccount<TezosAccount>(TezosConfig.Xtz);

            var fa12Address = await tezosAccount
                .DataRepository
                .GetTezosTokenAddressAsync("FA12", tokenContract, tokenId, address);

            if (fa12Address != null)
                return fa12Address;

            var fa2Address = await tezosAccount
                .DataRepository
                .GetTezosTokenAddressAsync("FA2", tokenContract, tokenId, address);

            return fa2Address;
        }

        private void UpdateFromAddressList(string from, string tokenContract, decimal tokenId)
        {
            _fromAddresses = new ObservableCollection<WalletAddressViewModel>(GetFromAddressList(tokenContract, tokenId));

            var tempFrom = from;

            if (tempFrom == null)
            {
                var unspentAddresses = _fromAddresses.Where(w => w.AvailableBalance > 0);
                var unspentTokenAddresses = _fromAddresses.Where(w => w.TokenBalance > 0);

                tempFrom = unspentTokenAddresses.MaxByOrDefault(w => w.TokenBalance)?.Address ??
                    unspentAddresses.MaxByOrDefault(w => w.AvailableBalance)?.Address;
            }
            
            From = tempFrom;
        }

        private async void UpdateCurrencyCode()
        {
            if (TokenContract == null || From == null)
                return;

            var tokenAddress = await GetTokenAddressAsync(_app.Account, From, TokenContract, TokenId);

            if (tokenAddress?.TokenBalance?.Symbol != null)
            {
                CurrencyCode = tokenAddress.TokenBalance.Symbol;
                CurrencyFormat = $"F{Math.Min(tokenAddress.TokenBalance.Decimals, MaxCurrencyDecimals)}";
            }
            else
            {
                CurrencyCode = _app.Account.Currencies
                    .FirstOrDefault(c => c is Fa12Config fa12 && fa12.TokenContractAddress == TokenContract)
                    ?.Name ?? "TOKENS";
                CurrencyFormat = DefaultCurrencyFormat;
            }

            CallUIRefresh();
        }

        private IEnumerable<WalletAddressViewModel> GetFromAddressList(string tokenContract, decimal tokenId)
        {
            if (tokenContract == null)
                return Enumerable.Empty<WalletAddressViewModel>();

            var tezosConfig = _app.Account
                .Currencies
                .Get<TezosConfig>(TezosConfig.Xtz);

            var tezosAccount = _app.Account
                .GetCurrencyAccount<TezosAccount>(TezosConfig.Xtz);

            var tezosAddresses = tezosAccount
                .GetUnspentAddressesAsync()
                .WaitForResult()
                .ToDictionary(w => w.Address, w => w);

            var tokenAddresses = tezosAccount.DataRepository
                .GetTezosTokenAddressesByContractAsync(tokenContract)
                .WaitForResult();

            return tokenAddresses
                .Where(w => w.Balance != 0)
                .Select(w =>
                {
                    var tokenBalance = w.Balance;

                    var showTokenBalance = tokenBalance != 0;

                    var tokenCode = w.TokenBalance?.Symbol ?? "TOKENS";

                    var tezosBalance = tezosAddresses.TryGetValue(w.Address, out var tezosAddress)
                        ? tezosAddress.AvailableBalance()
                        : 0m;

                    return new WalletAddressViewModel
                    {
                        Address          = w.Address,
                        AvailableBalance = tezosBalance,
                        CurrencyFormat   = tezosConfig.Format,
                        CurrencyCode     = tezosConfig.Name,
                        IsFreeAddress    = false,
                        ShowTokenBalance = showTokenBalance,
                        TokenBalance     = tokenBalance,
                        TokenFormat      = "F8",
                        TokenCode        = tokenCode
                    };
                })
                .ToList();
        }
    }
}