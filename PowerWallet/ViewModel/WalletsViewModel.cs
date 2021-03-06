﻿using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Messaging;
using NBitcoin;
using PowerWallet.Controls;
using PowerWallet.Messages;
using QBitNinja.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace PowerWallet.ViewModel
{
    public class WalletsViewModel : PWViewModelBase
    {
        private IStorage _Storage;
        const string WALLETS_KEY = "Opened-Wallets";

        public WalletsViewModel(QBitNinjaClientFactory factory, IStorage storage)
        {
            _ClientFactory = factory;
            _Storage = storage;
            var unused = LoadCache();
        }


        private AsyncCommand _Refresh;
        public AsyncCommand Refresh
        {
            get
            {
                if (_Refresh == null)
                {
                    _Refresh = new AsyncCommand(async _ =>
                    {
                        var refreshs = Wallets.Select(w => w.Refresh.ExecuteAsync(false)).ToArray();
                        await Task.WhenAll(refreshs);
                    });
                }
                return _Refresh;
            }
        }
        private async Task LoadCache()
        {
            var wallets = (await _Storage.Get<string[]>(WALLETS_KEY)) ?? new string[0];
            foreach (var wallet in wallets)
            {
                AddWallet(wallet, false);
            }
        }



        private readonly QBitNinjaClientFactory _ClientFactory;
        public QBitNinjaClientFactory ClientFactory
        {
            get
            {
                return _ClientFactory;
            }
        }

        private readonly ObservableCollection<WalletViewModel> _Wallets = new ObservableCollection<WalletViewModel>();
        public ObservableCollection<WalletViewModel> Wallets
        {
            get
            {
                return _Wallets;
            }
        }

        public class NewWalletCommand : AsyncCommand
        {
            private WalletsViewModel _Parent;
            public NewWalletCommand(WalletsViewModel parent)
            {
                this._Parent = parent;
            }

            private string _Name = "";
            public string Name
            {
                get
                {
                    return _Name;
                }
                set
                {
                    if (value != _Name)
                    {
                        _Name = value;
                        OnPropertyChanged(() => this.Name);
                    }
                }
            }

            private bool _IsColored;
            public bool IsColored
            {
                get
                {
                    return _IsColored;
                }
                set
                {
                    if (value != _IsColored)
                    {
                        _IsColored = value;
                        OnPropertyChanged(() => this.IsColored);
                    }
                }
            }

            protected override async Task CreateTask(System.Threading.CancellationToken cancelation)
            {
                var client = _Parent.ClientFactory.CreateClient();
                _Parent.AddWallet(await client.CreateWallet(Name), true);
            }
        }

        public ICommand CreateNewWalletCommand()
        {
            return new NewWalletCommand(this).Notify(MessengerInstance);
        }

        internal ICommand CreateOpenWalletCommand()
        {
            return new OpenWalletCommand(this).Notify(MessengerInstance);
        }

        private void AddWallet(string wallet, bool save)
        {
            AddWallet(new WalletModel()
            {
                Name = wallet
            }, save);
        }
        internal void AddWallet(WalletModel model, bool save)
        {
            if (!this.Wallets.Any(w => w.Name.Equals(model.Name, StringComparison.OrdinalIgnoreCase)))
            {
                var wallet = new WalletViewModel(model.Name, this);
                this.Wallets.Add(wallet);
                if (save)
                {
                    Save();
                }
            }
            this.Refresh.Execute();
        }

        internal void Save()
        {
            var unused = _Storage.Put(WALLETS_KEY, Wallets.Select(w => w.Name).ToArray());
        }


        private WalletViewModel _SelectedWallet;
        public WalletViewModel SelectedWallet
        {
            get
            {
                return _SelectedWallet;
            }
            set
            {
                if (value != _SelectedWallet)
                {
                    _SelectedWallet = value;
                    OnPropertyChanged(() => this.SelectedWallet);
                }
            }
        }

        public Network Network
        {
            get
            {
                return _ClientFactory.Network;
            }
        }
    }
    public class OpenWalletCommand : AsyncCommand
    {
        private WalletsViewModel _Parent;

        public OpenWalletCommand(WalletsViewModel parent)
        {
            this._Parent = parent;
        }

        private string _Name;
        public string Name
        {
            get
            {
                return _Name;
            }
            set
            {
                if (value != _Name)
                {
                    _Name = value;
                    OnPropertyChanged(() => this.Name);
                }
            }
        }

        protected override async Task CreateTask(System.Threading.CancellationToken cancelation)
        {
            var client = _Parent.ClientFactory.CreateClient();
            var model = await client.GetWallet(Name);
            if (model == null)
            {
                Error("Wallet does not exist");
            }
            else
            {
                _Parent.AddWallet(model, true);
            }
        }
    }
    public class WalletViewModel : PWViewModelBase
    {
        internal WalletsViewModel _Parent;
        public WalletViewModel(string name,
                               WalletsViewModel parent)
        {
            _Name = name;
            _Parent = parent;
        }

        private readonly string _Name;
        public string Name
        {
            get
            {
                return _Name;
            }
        }

        private readonly ObservableCollection<KeySetViewModel> _KeySets = new ObservableCollection<KeySetViewModel>();
        public ObservableCollection<KeySetViewModel> KeySets
        {
            get
            {
                return _KeySets;
            }
        }


        private AsyncCommand _Refresh;
        public AsyncCommand Refresh
        {
            get
            {
                if (_Refresh == null)
                {
                    _Refresh = new AsyncCommand(async _ =>
                    {
                        var rb = _Parent.ClientFactory.CreateClient();
                        var keysets = await rb.GetWalletClient(Name).GetKeySets();
                        KeySets.Clear();
                        foreach (var keyset in keysets)
                        {
                            KeySets.Add(new KeySetViewModel(this, keyset));
                        }
                        if (_Parent.SelectedWallet != null && _Parent.SelectedWallet.Name == this.Name)
                        {
                            await RefreshAddresses.ExecuteAsync(false);
                        }
                    })
                    .Notify(MessengerInstance);
                }
                return _Refresh;
            }
        }

        ICommand _Remove;
        public ICommand Remove
        {
            get
            {
                if (_Remove == null)
                {
                    _Remove = new RelayCommand(() =>
                    {
                        _Parent.Wallets.Remove(this);
                        _Parent.Save();
                    });
                }
                return _Remove;
            }
        }

        AsyncCommand _RefreshAddresses;
        public AsyncCommand RefreshAddresses
        {
            get
            {
                if (_RefreshAddresses == null)
                {
                    _RefreshAddresses = new AsyncCommand(async (_) =>
                    {
                        var rb = _Parent.ClientFactory.CreateClient();
                        var addresses = await rb.GetAddresses(Name);
                        Addresses.Clear();
                        foreach (var address in addresses)
                        {
                            Addresses.Add(new AddressViewModel(address, MessengerInstance));
                        }
                    })
                    .Notify(MessengerInstance);
                }
                return _RefreshAddresses;
            }
        }

        private readonly ObservableCollection<AddressViewModel> _Addresses = new ObservableCollection<AddressViewModel>();
        public ObservableCollection<AddressViewModel> Addresses
        {
            get
            {
                return _Addresses;
            }
        }

        public void Select()
        {
            MessengerInstance.Send(new ShowCoinsMessage(_Name));
            _Parent.SelectedWallet = this;
            RefreshAddresses.Execute();
        }

        public NewKeySetViewModel CreateNewKeysetCommand()
        {
            return (NewKeySetViewModel)new NewKeySetViewModel(this, _Parent.ClientFactory.Network).Notify(MessengerInstance);
        }


        public class NewAddressCommand : AsyncCommand
        {
            private WalletViewModel _Parent;

            public NewAddressCommand(WalletViewModel parent)
            {
                this._Parent = parent;
            }

            private BitcoinAddress _Address;
            public string Address
            {
                get
                {
                    return _Address == null ? null : _Address.ToString();
                }
                set
                {
                    var address = BitcoinAddress.Create(value, _Parent._Parent.Network);
                    if (address != _Address)
                    {
                        _Address = address;
                        OnPropertyChanged(() => this.Address);
                        OnCanExecuteChanged();
                    }
                }
            }
            protected override bool CanExecuteCore(object parameter)
            {
                return Address != null;
            }

            protected override async Task CreateTask(System.Threading.CancellationToken cancelation)
            {
                var rb = _Parent._Parent.ClientFactory.CreateClient();
                await rb.CreateAddress(_Parent.Name, new InsertWalletAddress()
                {
                    Address = _Address,
                    MergePast = true
                });
                _Parent.RefreshAddresses.Execute(false);
            }
        }

        public ICommand CreateNewAddressCommand()
        {
            return new NewAddressCommand(this);
        }
    }

    public class KeySetViewModel : PWViewModelBase
    {
        KeySetData _Keyset;
        WalletViewModel _Wallet;
        public KeySetViewModel(WalletViewModel parent, KeySetData keyset)
        {
            _Keyset = keyset;
            _Name = keyset.KeySet.Name;
            _Wallet = parent;
        }
        private readonly string _Name;
        public string Name
        {
            get
            {
                return _Name;
            }
        }

        AsyncCommand _Generate;
        public AsyncCommand Generate
        {
            get
            {
                if (_Generate == null)
                    _Generate = new AsyncCommand(async _ =>
                    {
                        var rb = _Wallet._Parent.ClientFactory.CreateClient();
                        var result =
                            await rb
                            .GetWalletClient(_Wallet.Name)
                            .GetKeySetClient(Name)
                            .GenerateKey();
                        _.Info("Address copied in clipboard : " + result.Address);
                        Clipboard.SetText(result.Address.ToString());
                        await _Wallet.RefreshAddresses.ExecuteAsync(false);
                    }).Notify(MessengerInstance);
                return _Generate;
            }
        }

        public KeySetPropertyViewModel ForPropertyGrid()
        {
            return new KeySetPropertyViewModel(_Keyset);
        }

        public void Select()
        {
            MessengerInstance.Send<ExposePropertiesMessage>(new ExposePropertiesMessage(ForPropertyGrid()));
        }
    }

    public class KeySetPropertyViewModel : PropertyViewModel
    {

        public KeySetPropertyViewModel(KeySetData keyset)
        {
            Name = keyset.KeySet.Name;
            Path = keyset.KeySet.Path == null ? "" : keyset.KeySet.Path.ToString();
            SignatureCount = keyset.KeySet.SignatureCount;
            CurrentPath = keyset.State.CurrentPath;
            int keyIndex = 0;
            foreach (var key in keyset.KeySet.ExtPubKeys.Select(k => k.ToString()))
            {
                keyIndex++;
                NewProperty()
                    .SetDisplay("Key " + keyIndex)
                    .SetEditor(typeof(ReadOnlyTextEditor))
                    .SetCategory("General")
                    .Commit()
                    .SetValue(key);
            }
        }

        [Editor(typeof(ReadOnlyTextEditor), typeof(ReadOnlyTextEditor))]
        [Category("General")]
        public string Name
        {
            get;
            set;
        }
        [Editor(typeof(ReadOnlyTextEditor), typeof(ReadOnlyTextEditor))]
        [Category("General")]
        public string Path
        {
            get;
            set;
        }
        [Editor(typeof(ReadOnlyTextEditor), typeof(ReadOnlyTextEditor))]
        [Category("General")]
        public int SignatureCount
        {
            get;
            set;
        }

        [Editor(typeof(ReadOnlyTextEditor), typeof(ReadOnlyTextEditor))]
        [Category("Current State")]
        public NBitcoin.KeyPath CurrentPath
        {
            get;
            set;
        }
    }

    public class AddressViewModel
    {
        IMessenger _Messenger;
        public AddressViewModel(WalletAddress address, IMessenger messenger)
        {
            _Messenger = messenger;
            Address = address.Address.ToString();
            if (address.KeysetData != null)
            {
                Keyset = address.KeysetData.KeySet.Name;
                Path = address.KeysetData.State.CurrentPath.ToString();
            }
        }

        [Editor(typeof(ReadOnlyTextEditor), typeof(ReadOnlyTextEditor))]
        public string Address
        {
            get;
            set;
        }
        [Editor(typeof(ReadOnlyTextEditor), typeof(ReadOnlyTextEditor))]
        public string Keyset
        {
            get;
            set;
        }
        [Editor(typeof(ReadOnlyTextEditor), typeof(ReadOnlyTextEditor))]
        public string Path
        {
            get;
            set;
        }

        public void ShowAddress()
        {
            _Messenger.Send(new SearchMessage(Address));
            _Messenger.Send(new ShowCoinsMessage(Address));
        }
    }
}
