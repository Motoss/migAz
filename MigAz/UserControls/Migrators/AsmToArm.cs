﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MigAz.Providers;
using MigAz.Azure;
using MigAz.Azure.Arm;
using MigAz.Core.Interface;
using MigAz.Azure.Generator.AsmToArm;
using MigAz.Core;
using MigAz.Forms;
using MigAz.Azure.UserControls;
using System.Xml;

namespace MigAz.UserControls.Migrators
{
    public partial class AsmToArm : IMigratorUserControl
    {
        #region Variables

        private UISaveSelectionProvider _saveSelectionProvider;
        private TreeNode _SourceAsmNode;
        private TreeNode _SourceArmNode;
        private List<TreeNode> _SelectedNodes = new List<TreeNode>();
        private AzureTelemetryProvider _telemetryProvider;
        private AppSettingsProvider _appSettingsProvider;
        private AzureContext _AzureContextSourceASM;
        private AzureContext _AzureContextTargetARM;
        private List<Azure.MigrationTarget.NetworkSecurityGroup> _AsmTargetNetworkSecurityGroups;
        private List<Azure.MigrationTarget.StorageAccount> _AsmTargetStorageAccounts;
        private List<Azure.MigrationTarget.VirtualNetwork> _AsmTargetVirtualNetworks;
        private List<Azure.MigrationTarget.VirtualMachine> _AsmTargetVirtualMachines;
        private List<Azure.MigrationTarget.StorageAccount> _ArmTargetStorageAccounts;
        private List<Azure.MigrationTarget.VirtualNetwork> _ArmTargetVirtualNetworks;
        private List<Azure.MigrationTarget.VirtualMachine> _ArmTargetVirtualMachines;
        private List<Azure.MigrationTarget.Disk> _ArmTargetManagedDisks;
        private List<Azure.MigrationTarget.LoadBalancer> _ArmTargetLoadBalancers;
        private List<Azure.MigrationTarget.NetworkSecurityGroup> _ArmTargetNetworkSecurityGroups;
        private Azure.MigrationTarget.ResourceGroup _TargetResourceGroup;
        private PropertyPanel _PropertyPanel;

        #endregion

        #region Constructors

        public AsmToArm() : base(null, null) { }

        public AsmToArm(IStatusProvider statusProvider, ILogProvider logProvider, PropertyPanel propertyPanel) 
            : base (statusProvider, logProvider)
        {
            InitializeComponent();

            _saveSelectionProvider = new UISaveSelectionProvider();
            _telemetryProvider = new AzureTelemetryProvider();
            _appSettingsProvider = new AppSettingsProvider();
            _PropertyPanel = propertyPanel;

            _AzureContextSourceASM = new AzureContext(LogProvider, StatusProvider, _appSettingsProvider);
            _AzureContextSourceASM.AzureEnvironmentChanged += _AzureContextSourceASM_AzureEnvironmentChanged;
            _AzureContextSourceASM.UserAuthenticated += _AzureContextSourceASM_UserAuthenticated;
            _AzureContextSourceASM.BeforeAzureSubscriptionChange += _AzureContextSourceASM_BeforeAzureSubscriptionChange;
            _AzureContextSourceASM.AfterAzureSubscriptionChange += _AzureContextSourceASM_AfterAzureSubscriptionChange;
            _AzureContextSourceASM.BeforeUserSignOut += _AzureContextSourceASM_BeforeUserSignOut;
            _AzureContextSourceASM.AfterUserSignOut += _AzureContextSourceASM_AfterUserSignOut;
            _AzureContextSourceASM.AfterAzureTenantChange += _AzureContextSourceASM_AfterAzureTenantChange;

            _AzureContextTargetARM = new AzureContext(LogProvider, StatusProvider, _appSettingsProvider);
            _AzureContextTargetARM.AfterAzureSubscriptionChange += _AzureContextTargetARM_AfterAzureSubscriptionChange;

            _TargetResourceGroup = new Azure.MigrationTarget.ResourceGroup(this.AzureContextSourceASM);

            azureLoginContextViewerASM.Bind(_AzureContextSourceASM);
            azureLoginContextViewerARM.Bind(_AzureContextTargetARM);

            this.TemplateGenerator = new AzureGenerator(_AzureContextSourceASM.AzureSubscription, _AzureContextTargetARM.AzureSubscription, _TargetResourceGroup, LogProvider, StatusProvider, _telemetryProvider, _appSettingsProvider);
        }

        private async Task _AzureContextTargetARM_AfterAzureSubscriptionChange(AzureContext sender)
        {
            this.TemplateGenerator.TargetSubscription = sender.AzureSubscription;
        }

        private async Task _AzureContextSourceASM_AfterAzureTenantChange(AzureContext sender)
        {
            await _AzureContextTargetARM.CopyContext(_AzureContextSourceASM);
        }

        #endregion

        private async Task _AzureContextSourceASM_BeforeAzureSubscriptionChange(AzureContext sender)
        {
            await SaveSubscriptionSettings(sender.AzureSubscription);
            await _AzureContextTargetARM.SetSubscriptionContext(null);
        }

        private async Task _AzureContextSourceASM_AzureEnvironmentChanged(AzureContext sender)
        {
            app.Default.AzureEnvironment = sender.AzureEnvironment.ToString();
            app.Default.Save();

            if (_AzureContextTargetARM.TokenProvider == null)
                _AzureContextTargetARM.AzureEnvironment = sender.AzureEnvironment;
        }


        private async Task _AzureContextSourceASM_UserAuthenticated(AzureContext sender)
        {
            if (_AzureContextTargetARM.TokenProvider.AuthenticationResult == null)
            {
                await _AzureContextTargetARM.CopyContext(_AzureContextSourceASM);
            }
        }

        private async Task _AzureContextSourceASM_BeforeUserSignOut()
        {
            await SaveSubscriptionSettings(this._AzureContextSourceASM.AzureSubscription);
        }

        private async Task _AzureContextSourceASM_AfterUserSignOut()
        {
            ResetForm();
            await _AzureContextTargetARM.SetSubscriptionContext(null);
            await _AzureContextTargetARM.Logout();
            azureLoginContextViewerARM.Enabled = false;
            azureLoginContextViewerARM.Refresh();
        }

        private async Task _AzureContextSourceASM_AfterAzureSubscriptionChange(AzureContext sender)
        {
            ResetForm();

            try
            {
                if (sender.AzureSubscription != null)
                {
                    if (_AzureContextTargetARM.AzureSubscription == null)
                    {
                        await _AzureContextTargetARM.SetSubscriptionContext(_AzureContextSourceASM.AzureSubscription);
                    }

                    azureLoginContextViewerARM.Enabled = true;

                    this.TemplateGenerator.SourceSubscription = _AzureContextSourceASM.AzureSubscription;
                    this.TemplateGenerator.TargetSubscription = _AzureContextTargetARM.AzureSubscription;

                    #region Bind Source ASM Objects

                    _AsmTargetNetworkSecurityGroups = new List<Azure.MigrationTarget.NetworkSecurityGroup>();
                    _AsmTargetStorageAccounts = new List<Azure.MigrationTarget.StorageAccount>();
                    _AsmTargetVirtualNetworks = new List<Azure.MigrationTarget.VirtualNetwork>();
                    _AsmTargetVirtualMachines = new List<Azure.MigrationTarget.VirtualMachine>();

                    TreeNode subscriptionNodeASM = new TreeNode(sender.AzureSubscription.Name);
                    treeSourceASM.Nodes.Add(subscriptionNodeASM);
                    subscriptionNodeASM.Expand();

                    foreach (Azure.Asm.NetworkSecurityGroup asmNetworkSecurityGroup in await _AzureContextSourceASM.AzureRetriever.GetAzureAsmNetworkSecurityGroups())
                    {
                        TreeNode parentNode = GetDataCenterTreeViewNode(subscriptionNodeASM, asmNetworkSecurityGroup.Location, "Network Security Groups");

                        Azure.MigrationTarget.NetworkSecurityGroup targetNetworkSecurityGroup = new Azure.MigrationTarget.NetworkSecurityGroup(this.AzureContextTargetARM, asmNetworkSecurityGroup);
                        _AsmTargetNetworkSecurityGroups.Add(targetNetworkSecurityGroup);

                        TreeNode tnNetworkSecurityGroup = new TreeNode(targetNetworkSecurityGroup.SourceName);
                        tnNetworkSecurityGroup.Name = targetNetworkSecurityGroup.SourceName;
                        tnNetworkSecurityGroup.Tag = targetNetworkSecurityGroup;
                        parentNode.Nodes.Add(tnNetworkSecurityGroup);
                        parentNode.Expand();
                    }

                    List<Azure.Asm.VirtualNetwork> asmVirtualNetworks = await _AzureContextSourceASM.AzureRetriever.GetAzureAsmVirtualNetworks();
                    foreach (Azure.Asm.VirtualNetwork asmVirtualNetwork in asmVirtualNetworks)
                    {
                        TreeNode parentNode = GetDataCenterTreeViewNode(subscriptionNodeASM, asmVirtualNetwork.Location, "Virtual Networks");

                        Azure.MigrationTarget.VirtualNetwork targetVirtualNetwork = new Azure.MigrationTarget.VirtualNetwork(this.AzureContextTargetARM, asmVirtualNetwork, _AsmTargetNetworkSecurityGroups);
                        _AsmTargetVirtualNetworks.Add(targetVirtualNetwork);

                        TreeNode tnVirtualNetwork = new TreeNode(targetVirtualNetwork.SourceName);
                        tnVirtualNetwork.Name = targetVirtualNetwork.SourceName;
                        tnVirtualNetwork.Text = targetVirtualNetwork.SourceName;
                        tnVirtualNetwork.Tag = targetVirtualNetwork;
                        parentNode.Nodes.Add(tnVirtualNetwork);
                        parentNode.Expand();
                    }

                    foreach (Azure.Asm.StorageAccount asmStorageAccount in await _AzureContextSourceASM.AzureRetriever.GetAzureAsmStorageAccounts())
                    {
                        TreeNode parentNode = GetDataCenterTreeViewNode(subscriptionNodeASM, asmStorageAccount.GeoPrimaryRegion, "Storage Accounts");

                        Azure.MigrationTarget.StorageAccount targetStorageAccount = new Azure.MigrationTarget.StorageAccount(_AzureContextTargetARM, asmStorageAccount);
                        _AsmTargetStorageAccounts.Add(targetStorageAccount);

                        TreeNode tnStorageAccount = new TreeNode(targetStorageAccount.SourceName);
                        tnStorageAccount.Name = targetStorageAccount.SourceName;
                        tnStorageAccount.Tag = targetStorageAccount;
                        parentNode.Nodes.Add(tnStorageAccount);
                        parentNode.Expand();
                    }

                    List<Azure.Asm.CloudService> asmCloudServices = await _AzureContextSourceASM.AzureRetriever.GetAzureAsmCloudServices();
                    foreach (Azure.Asm.CloudService asmCloudService in asmCloudServices)
                    {
                        List<Azure.MigrationTarget.VirtualMachine> cloudServiceTargetVirtualMachines = new List<Azure.MigrationTarget.VirtualMachine>();
                        Azure.MigrationTarget.AvailabilitySet targetAvailabilitySet = new Azure.MigrationTarget.AvailabilitySet(_AzureContextTargetARM, asmCloudService);

                        TreeNode parentNode = GetDataCenterTreeViewNode(subscriptionNodeASM, asmCloudService.Location, "Cloud Services");
                        TreeNode[] cloudServiceNodeSearch = parentNode.Nodes.Find(asmCloudService.Name, false);
                        TreeNode cloudServiceNode = null;
                        if (cloudServiceNodeSearch.Count() == 1)
                        {
                            cloudServiceNode = cloudServiceNodeSearch[0];
                        }

                        if (cloudServiceNode == null)
                        {
                            cloudServiceNode = new TreeNode(asmCloudService.Name);
                            cloudServiceNode.Name = asmCloudService.Name;
                            cloudServiceNode.Tag = targetAvailabilitySet;
                            parentNode.Nodes.Add(cloudServiceNode);
                            parentNode.Expand();
                        }

                        foreach (Azure.Asm.VirtualMachine asmVirtualMachine in asmCloudService.VirtualMachines)
                        {
                            Azure.MigrationTarget.VirtualMachine targetVirtualMachine = new Azure.MigrationTarget.VirtualMachine(this.AzureContextTargetARM, asmVirtualMachine, _AsmTargetVirtualNetworks, _AsmTargetStorageAccounts, _AsmTargetNetworkSecurityGroups);
                            targetVirtualMachine.TargetAvailabilitySet = targetAvailabilitySet;
                            cloudServiceTargetVirtualMachines.Add(targetVirtualMachine);
                            _AsmTargetVirtualMachines.Add(targetVirtualMachine);

                            TreeNode virtualMachineNode = new TreeNode(targetVirtualMachine.SourceName);
                            virtualMachineNode.Name = targetVirtualMachine.SourceName;
                            virtualMachineNode.Tag = targetVirtualMachine;
                            cloudServiceNode.Nodes.Add(virtualMachineNode);
                            cloudServiceNode.Expand();
                        }

                        Azure.MigrationTarget.LoadBalancer targetLoadBalancer = new Azure.MigrationTarget.LoadBalancer();
                        targetLoadBalancer.Name = asmCloudService.Name;
                        targetLoadBalancer.SourceName = asmCloudService.Name + "-LB";

                        TreeNode loadBalancerNode = new TreeNode(targetLoadBalancer.SourceName);
                        loadBalancerNode.Name = targetLoadBalancer.SourceName;
                        loadBalancerNode.Tag = targetLoadBalancer;
                        cloudServiceNode.Nodes.Add(loadBalancerNode);
                        cloudServiceNode.Expand();

                        Azure.MigrationTarget.FrontEndIpConfiguration frontEndIpConfiguration = new Azure.MigrationTarget.FrontEndIpConfiguration(targetLoadBalancer);

                        Azure.MigrationTarget.BackEndAddressPool backEndAddressPool = new Azure.MigrationTarget.BackEndAddressPool(targetLoadBalancer);

                        // if internal load balancer
                        if (asmCloudService.ResourceXml.SelectNodes("//Deployments/Deployment/LoadBalancers/LoadBalancer/FrontendIpConfiguration/Type").Count > 0)
                        {
                            string virtualnetworkname = asmCloudService.ResourceXml.SelectSingleNode("//Deployments/Deployment/VirtualNetworkName").InnerText;
                            string subnetname = asmCloudService.ResourceXml.SelectSingleNode("//Deployments/Deployment/LoadBalancers/LoadBalancer/FrontendIpConfiguration/SubnetName").InnerText.Replace(" ", "");

                            if (asmCloudService.ResourceXml.SelectNodes("//Deployments/Deployment/LoadBalancers/LoadBalancer/FrontendIpConfiguration/StaticVirtualNetworkIPAddress").Count > 0)
                            {
                                frontEndIpConfiguration.PrivateIPAllocationMethod = "Static";
                                frontEndIpConfiguration.PrivateIPAddress = asmCloudService.ResourceXml.SelectSingleNode("//Deployments/Deployment/LoadBalancers/LoadBalancer/FrontendIpConfiguration/StaticVirtualNetworkIPAddress").InnerText;
                            }

                            if (cloudServiceTargetVirtualMachines.Count > 0)
                            {
                                if (cloudServiceTargetVirtualMachines[0].PrimaryNetworkInterface != null)
                                {
                                    if (cloudServiceTargetVirtualMachines[0].PrimaryNetworkInterface.TargetNetworkInterfaceIpConfigurations.Count > 0)
                                    {
                                        frontEndIpConfiguration.TargetVirtualNetwork = cloudServiceTargetVirtualMachines[0].PrimaryNetworkInterface.TargetNetworkInterfaceIpConfigurations[0].TargetVirtualNetwork;
                                        frontEndIpConfiguration.TargetSubnet = cloudServiceTargetVirtualMachines[0].PrimaryNetworkInterface.TargetNetworkInterfaceIpConfigurations[0].TargetSubnet;
                                    }
                                }
                            }
                        }
                        else // if external load balancer
                        {
                            Azure.MigrationTarget.PublicIp loadBalancerPublicIp = new Azure.MigrationTarget.PublicIp();
                            loadBalancerPublicIp.SourceName = asmCloudService.Name + "-PIP";
                            loadBalancerPublicIp.Name = asmCloudService.Name;
                            loadBalancerPublicIp.DomainNameLabel = asmCloudService.Name;
                            frontEndIpConfiguration.PublicIp = loadBalancerPublicIp;

                            TreeNode publicIPAddressNode = new TreeNode(loadBalancerPublicIp.SourceName);
                            publicIPAddressNode.Name = loadBalancerPublicIp.SourceName;
                            publicIPAddressNode.Tag = loadBalancerPublicIp;
                            cloudServiceNode.Nodes.Add(publicIPAddressNode);
                            cloudServiceNode.Expand();
                        }

                        foreach (Azure.MigrationTarget.VirtualMachine targetVirtualMachine in cloudServiceTargetVirtualMachines)
                        {
                            if (targetVirtualMachine.PrimaryNetworkInterface != null)
                                targetVirtualMachine.PrimaryNetworkInterface.BackEndAddressPool = backEndAddressPool;

                            Azure.Asm.VirtualMachine asmVirtualMachine = (Azure.Asm.VirtualMachine)targetVirtualMachine.Source;
                            foreach (XmlNode inputendpoint in asmVirtualMachine.ResourceXml.SelectNodes("//ConfigurationSets/ConfigurationSet/InputEndpoints/InputEndpoint"))
                            {
                                if (inputendpoint.SelectSingleNode("LoadBalancedEndpointSetName") == null) // if it's a inbound nat rule
                                {
                                    Azure.MigrationTarget.InboundNatRule targetInboundNatRule = new Azure.MigrationTarget.InboundNatRule(targetLoadBalancer);
                                    targetInboundNatRule.Name = asmVirtualMachine.RoleName + "-" + inputendpoint.SelectSingleNode("Name").InnerText;
                                    targetInboundNatRule.FrontEndPort = Int32.Parse(inputendpoint.SelectSingleNode("Port").InnerText);
                                    targetInboundNatRule.BackEndPort = Int32.Parse(inputendpoint.SelectSingleNode("LocalPort").InnerText);
                                    targetInboundNatRule.Protocol = inputendpoint.SelectSingleNode("Protocol").InnerText;
                                    targetInboundNatRule.FrontEndIpConfiguration = frontEndIpConfiguration;

                                    if (targetVirtualMachine.PrimaryNetworkInterface != null)
                                        targetVirtualMachine.PrimaryNetworkInterface.InboundNatRules.Add(targetInboundNatRule);
                                }
                                else // if it's a load balancing rule
                                {
                                    XmlNode probenode = inputendpoint.SelectSingleNode("LoadBalancerProbe");

                                    Azure.MigrationTarget.Probe targetProbe = null;
                                    foreach (Azure.MigrationTarget.Probe existingProbe in targetLoadBalancer.Probes)
                                    {
                                        if (existingProbe.Name == inputendpoint.SelectSingleNode("LoadBalancedEndpointSetName").InnerText)
                                        {
                                            targetProbe = existingProbe;
                                            break;
                                        }
                                    }

                                    if (targetProbe == null)
                                    {
                                        targetProbe = new Azure.MigrationTarget.Probe();
                                        targetProbe.Name = inputendpoint.SelectSingleNode("LoadBalancedEndpointSetName").InnerText;
                                        targetProbe.Port = Int32.Parse(probenode.SelectSingleNode("Port").InnerText);
                                        targetProbe.Protocol = probenode.SelectSingleNode("Protocol").InnerText;
                                    }

                                    Azure.MigrationTarget.LoadBalancingRule targetLoadBalancingRule = null;
                                    foreach (Azure.MigrationTarget.LoadBalancingRule existingLoadBalancingRule in targetLoadBalancer.LoadBalancingRules)
                                    {
                                        if (existingLoadBalancingRule.Name == inputendpoint.SelectSingleNode("LoadBalancedEndpointSetName").InnerText)
                                        {
                                            targetLoadBalancingRule = existingLoadBalancingRule;
                                            break;
                                        }
                                    }

                                    if (targetLoadBalancingRule == null)
                                    {
                                        targetLoadBalancingRule = new Azure.MigrationTarget.LoadBalancingRule(targetLoadBalancer);
                                        targetLoadBalancingRule.Name = inputendpoint.SelectSingleNode("LoadBalancedEndpointSetName").InnerText;
                                        targetLoadBalancingRule.FrontEndIpConfiguration = frontEndIpConfiguration;
                                        targetLoadBalancingRule.BackEndAddressPool = targetLoadBalancer.BackEndAddressPools[0];
                                        targetLoadBalancingRule.Probe = targetProbe;
                                        targetLoadBalancingRule.FrontEndPort = Int32.Parse(inputendpoint.SelectSingleNode("Port").InnerText);
                                        targetLoadBalancingRule.BackEndPort = Int32.Parse(inputendpoint.SelectSingleNode("LocalPort").InnerText);
                                        targetLoadBalancingRule.Protocol = inputendpoint.SelectSingleNode("Protocol").InnerText;
                                    }
                                }
                            }
                        }

                    }

                    subscriptionNodeASM.ExpandAll();

                    #endregion

                    #region Bind Source ARM Objects

                    _ArmTargetStorageAccounts = new List<Azure.MigrationTarget.StorageAccount>();
                    _ArmTargetVirtualNetworks = new List<Azure.MigrationTarget.VirtualNetwork>();
                    _ArmTargetNetworkSecurityGroups = new List<Azure.MigrationTarget.NetworkSecurityGroup>();
                    _ArmTargetVirtualMachines = new List<Azure.MigrationTarget.VirtualMachine>();
                    _ArmTargetLoadBalancers = new List<Azure.MigrationTarget.LoadBalancer>();

                    TreeNode subscriptionNodeARM = new TreeNode(sender.AzureSubscription.Name);
                    subscriptionNodeARM.ImageKey = "Subscription";
                    subscriptionNodeARM.SelectedImageKey = "Subscription";
                    treeSourceARM.Nodes.Add(subscriptionNodeARM);
                    subscriptionNodeARM.Expand();
                    foreach (Azure.Arm.ResourceGroup armResourceGroup in await _AzureContextSourceASM.AzureRetriever.GetAzureARMResourceGroups())
                    {
                        TreeNode tnResourceGroup = GetResourceGroupTreeNode(subscriptionNodeARM, armResourceGroup);
                    }

                    foreach (Azure.Arm.NetworkSecurityGroup armNetworkSecurityGroup in await _AzureContextSourceASM.AzureRetriever.GetAzureARMNetworkSecurityGroups())
                    {
                        TreeNode networkSecurityGroupParentNode = subscriptionNodeARM;

                        if (armNetworkSecurityGroup.ResourceGroup != null)
                        {
                            TreeNode tnResourceGroup = GetResourceGroupTreeNode(subscriptionNodeARM, armNetworkSecurityGroup.ResourceGroup);
                            networkSecurityGroupParentNode = tnResourceGroup;
                        }

                        Azure.MigrationTarget.NetworkSecurityGroup targetNetworkSecurityGroup = new Azure.MigrationTarget.NetworkSecurityGroup(this.AzureContextTargetARM, armNetworkSecurityGroup);
                        _ArmTargetNetworkSecurityGroups.Add(targetNetworkSecurityGroup);

                        TreeNode tnNetworkSecurityGroup = new TreeNode(targetNetworkSecurityGroup.SourceName);
                        tnNetworkSecurityGroup.Name = targetNetworkSecurityGroup.SourceName;
                        tnNetworkSecurityGroup.Tag = targetNetworkSecurityGroup;
                        tnNetworkSecurityGroup.ImageKey = "NetworkSecurityGroup";
                        tnNetworkSecurityGroup.SelectedImageKey = "NetworkSecurityGroup";
                        networkSecurityGroupParentNode.Nodes.Add(tnNetworkSecurityGroup);
                        networkSecurityGroupParentNode.Expand();
                    }

                    foreach (Azure.Arm.VirtualNetwork armVirtualNetwork in await _AzureContextSourceASM.AzureRetriever.GetAzureARMVirtualNetworks())
                    {
                        TreeNode virtualNetworkParentNode = subscriptionNodeARM;

                        if (armVirtualNetwork.ResourceGroup != null)
                        {
                            TreeNode tnResourceGroup = GetResourceGroupTreeNode(subscriptionNodeARM, armVirtualNetwork.ResourceGroup);
                            virtualNetworkParentNode = tnResourceGroup;
                        }

                        Azure.MigrationTarget.VirtualNetwork targetVirtualNetwork = new Azure.MigrationTarget.VirtualNetwork(this.AzureContextTargetARM, armVirtualNetwork, _ArmTargetNetworkSecurityGroups);
                        _ArmTargetVirtualNetworks.Add(targetVirtualNetwork);

                        TreeNode tnVirtualNetwork = new TreeNode(targetVirtualNetwork.SourceName);
                        tnVirtualNetwork.Name = targetVirtualNetwork.SourceName;
                        tnVirtualNetwork.Tag = targetVirtualNetwork;
                        tnVirtualNetwork.ImageKey = "VirtualNetwork";
                        tnVirtualNetwork.SelectedImageKey = "VirtualNetwork";
                        virtualNetworkParentNode.Nodes.Add(tnVirtualNetwork);
                        virtualNetworkParentNode.Expand();
                    }

                    foreach (Azure.Arm.StorageAccount armStorageAccount in await _AzureContextSourceASM.AzureRetriever.GetAzureARMStorageAccounts())
                    {
                        TreeNode storageAccountParentNode = subscriptionNodeARM;

                        if (armStorageAccount.ResourceGroup != null)
                        {
                            TreeNode tnResourceGroup = GetResourceGroupTreeNode(subscriptionNodeARM, armStorageAccount.ResourceGroup);
                            tnResourceGroup.ImageKey = "ResourceGroup";
                            tnResourceGroup.SelectedImageKey = "ResourceGroup";
                            storageAccountParentNode = tnResourceGroup;
                        }

                        Azure.MigrationTarget.StorageAccount targetStorageAccount = new Azure.MigrationTarget.StorageAccount(_AzureContextTargetARM, armStorageAccount);
                        _ArmTargetStorageAccounts.Add(targetStorageAccount);

                        TreeNode tnStorageAccount = new TreeNode(targetStorageAccount.SourceName);
                        tnStorageAccount.Name = targetStorageAccount.SourceName;
                        tnStorageAccount.Tag = targetStorageAccount;
                        tnStorageAccount.ImageKey = "StorageAccount";
                        tnStorageAccount.SelectedImageKey = "StorageAccount";
                        storageAccountParentNode.Nodes.Add(tnStorageAccount);
                        storageAccountParentNode.Expand();
                    }

                    try
                    {
                        foreach (Azure.Arm.ManagedDisk armManagedDisk in await _AzureContextSourceASM.AzureRetriever.GetAzureARMManagedDisks())
                        {
                            Azure.MigrationTarget.Disk targetManagedDisk = new Azure.MigrationTarget.Disk(armManagedDisk);
                            _ArmTargetManagedDisks.Add(targetManagedDisk);
                        }
                    }
                    catch (Exception exc)
                    {
                        // todo, this is being caught, because Managed Disks fail in Azure Government with 404 error (not yet available)
                    }

                    foreach (Azure.Arm.VirtualMachine armVirtualMachine in await _AzureContextSourceASM.AzureRetriever.GetAzureArmVirtualMachines())
                    {
                        TreeNode virtualMachineParentNode = subscriptionNodeARM;

                        TreeNode tnResourceGroup = GetResourceGroupTreeNode(subscriptionNodeARM, armVirtualMachine.ResourceGroup);
                        virtualMachineParentNode = tnResourceGroup;

                        Azure.MigrationTarget.VirtualMachine targetVirtualMachine = new Azure.MigrationTarget.VirtualMachine(this.AzureContextTargetARM, armVirtualMachine, _ArmTargetVirtualNetworks, _ArmTargetStorageAccounts, _ArmTargetNetworkSecurityGroups);
                        _ArmTargetVirtualMachines.Add(targetVirtualMachine);

                        if (armVirtualMachine.AvailabilitySet != null)
                        {
                            Azure.MigrationTarget.AvailabilitySet targetAvailabilitySet = new Azure.MigrationTarget.AvailabilitySet(this.AzureContextTargetARM, armVirtualMachine.AvailabilitySet);
                            TreeNode tnAvailabilitySet = GetAvailabilitySetTreeNode(virtualMachineParentNode, targetAvailabilitySet);
                            targetVirtualMachine.TargetAvailabilitySet = (Azure.MigrationTarget.AvailabilitySet)tnAvailabilitySet.Tag;
                            virtualMachineParentNode = tnAvailabilitySet;
                        }

                        TreeNode tnVirtualMachine = new TreeNode(targetVirtualMachine.SourceName);
                        tnVirtualMachine.Name = targetVirtualMachine.SourceName;
                        tnVirtualMachine.Tag = targetVirtualMachine;
                        tnVirtualMachine.ImageKey = "VirtualMachine";
                        tnVirtualMachine.SelectedImageKey = "VirtualMachine";
                        virtualMachineParentNode.Nodes.Add(tnVirtualMachine);
                        virtualMachineParentNode.Expand();
                    }

                    foreach (Azure.Arm.NetworkSecurityGroup armNetworkSecurityGroup in await _AzureContextSourceASM.AzureRetriever.GetAzureARMNetworkSecurityGroups())
                    {
                        TreeNode networkSecurityGroupParentNode = subscriptionNodeARM;

                        TreeNode tnResourceGroup = GetResourceGroupTreeNode(subscriptionNodeARM, armNetworkSecurityGroup.ResourceGroup);
                        networkSecurityGroupParentNode = tnResourceGroup;

                        Azure.MigrationTarget.NetworkSecurityGroup targetNetworkSecurityGroup = new Azure.MigrationTarget.NetworkSecurityGroup(this.AzureContextTargetARM, armNetworkSecurityGroup);
                        _ArmTargetNetworkSecurityGroups.Add(targetNetworkSecurityGroup);

                        TreeNode tnNetworkSecurityGroup = new TreeNode(targetNetworkSecurityGroup.SourceName);
                        tnNetworkSecurityGroup.Name = targetNetworkSecurityGroup.SourceName;
                        tnNetworkSecurityGroup.Tag = targetNetworkSecurityGroup;
                        tnNetworkSecurityGroup.ImageKey = "NetworkSecurityGroup";
                        tnNetworkSecurityGroup.SelectedImageKey = "NetworkSecurityGroup";
                        networkSecurityGroupParentNode.Nodes.Add(tnNetworkSecurityGroup);
                        networkSecurityGroupParentNode.Expand();
                    }

                    foreach (Azure.Arm.LoadBalancer armLoadBalancer in await _AzureContextSourceASM.AzureRetriever.GetAzureARMLoadBalancers())
                    {
                        TreeNode networkSecurityGroupParentNode = subscriptionNodeARM;

                        TreeNode tnResourceGroup = GetResourceGroupTreeNode(subscriptionNodeARM, armLoadBalancer.ResourceGroup);
                        networkSecurityGroupParentNode = tnResourceGroup;

                        Azure.MigrationTarget.LoadBalancer targetLoadBalancer = new Azure.MigrationTarget.LoadBalancer(armLoadBalancer);
                        _ArmTargetLoadBalancers.Add(targetLoadBalancer);

                        TreeNode tnNetworkSecurityGroup = new TreeNode(targetLoadBalancer.SourceName);
                        tnNetworkSecurityGroup.Name = targetLoadBalancer.SourceName;
                        tnNetworkSecurityGroup.Tag = targetLoadBalancer;
                        tnNetworkSecurityGroup.ImageKey = "LoadBalancer";
                        tnNetworkSecurityGroup.SelectedImageKey = "LoadBalancer";
                        networkSecurityGroupParentNode.Nodes.Add(tnNetworkSecurityGroup);
                        networkSecurityGroupParentNode.Expand();
                    }


                    subscriptionNodeARM.ExpandAll();

                    #endregion

                    _AzureContextSourceASM.AzureRetriever.SaveRestCache();
                    await ReadSubscriptionSettings(sender.AzureSubscription);

                    treeSourceASM.Enabled = true;
                    treeSourceARM.Enabled = true;
                    treeTargetARM.Enabled = true;
                }
            }
            catch (Exception exc)
            {
                UnhandledExceptionDialog unhandledException = new UnhandledExceptionDialog(LogProvider, exc);
                unhandledException.ShowDialog();
            }
            
            StatusProvider.UpdateStatus("Ready");
        }

        internal void ActivateSourceARMTab()
        {
            tabSourceResources.SelectedTab = tabSourceResources.TabPages[1];
        }

        internal void ChangeAzureContext()
        {
            azureLoginContextViewerASM.ChangeAzureContext();
        }

        private void ResetForm()
        {
            treeSourceASM.Nodes.Clear();
            treeSourceARM.Nodes.Clear();
            treeTargetARM.Nodes.Clear();
            _SelectedNodes.Clear();
            UpdateExportItemsCount();
            _PropertyPanel.Clear();
            treeSourceASM.Enabled = false;
            treeSourceARM.Enabled = false;
            treeTargetARM.Enabled = false;
        }

        #region Properties

        public Azure.MigrationTarget.ResourceGroup TargetResourceGroup
        {
            get { return _TargetResourceGroup; }
        }

        public AzureContext AzureContextSourceASM
        {
            get { return _AzureContextSourceASM; }
        }

        public AzureContext AzureContextTargetARM
        {
            get { return _AzureContextTargetARM; }
        }

        public MigAz.Core.Interface.ITelemetryProvider TelemetryProvider
        {
            get { return _telemetryProvider; }
        }

        internal AppSettingsProvider AppSettingsProviders
        {
            get { return _appSettingsProvider; }
        }

        internal List<TreeNode> SelectedNodes
        {
            get { return _SelectedNodes; }
        }

        #endregion

        #region New Version Check

        private async Task AlertIfNewVersionAvailable()
        {
            string currentVersion = "2.2.3.0";
            VersionCheck versionCheck = new VersionCheck(this.LogProvider);
            string newVersionNumber = await versionCheck.GetAvailableVersion("https://api.migaz.tools/v1/version", currentVersion);
            if (versionCheck.IsVersionNewer(currentVersion, newVersionNumber))
            {
                DialogResult dialogresult = MessageBox.Show("New version " + newVersionNumber + " is available at http://aka.ms/MigAz", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        #endregion

        #region ASM TreeView Methods

        private async Task AutoSelectDependencies(TreeNode selectedNode)
        {
            if ((app.Default.AutoSelectDependencies) && (selectedNode.Checked) && (selectedNode.Tag != null))
            {
                if (selectedNode.Tag.GetType() == typeof(Azure.MigrationTarget.VirtualMachine))
                {
                    Azure.MigrationTarget.VirtualMachine targetVirtualMachine = (Azure.MigrationTarget.VirtualMachine)selectedNode.Tag;

                    if (targetVirtualMachine.Source != null)
                    {
                        if (targetVirtualMachine.Source.GetType() == typeof(Azure.Asm.VirtualMachine))
                        {
                            Azure.Asm.VirtualMachine asmVirtualMachine = (Azure.Asm.VirtualMachine)targetVirtualMachine.Source;

                            #region process virtual network

                            foreach (Azure.MigrationTarget.NetworkInterface networkInterface in targetVirtualMachine.NetworkInterfaces)
                            {
                                #region Auto Select Virtual Network from each IpConfiguration

                                foreach (Azure.MigrationTarget.NetworkInterfaceIpConfiguration ipConfiguration in networkInterface.TargetNetworkInterfaceIpConfigurations)
                                {
                                    if (ipConfiguration.TargetVirtualNetwork != null && ipConfiguration.TargetVirtualNetwork.GetType() == typeof(Azure.MigrationTarget.VirtualNetwork))
                                    {
                                        Azure.MigrationTarget.VirtualNetwork targetVirtualNetwork = (Azure.MigrationTarget.VirtualNetwork)ipConfiguration.TargetVirtualNetwork;
                                        foreach (TreeNode treeNode in treeSourceASM.Nodes.Find(targetVirtualNetwork.SourceName, true))
                                        {
                                            if ((treeNode.Tag != null) && (treeNode.Tag.GetType() == typeof(Azure.MigrationTarget.VirtualNetwork)))
                                            {
                                                if (!treeNode.Checked)
                                                    treeNode.Checked = true;
                                            }
                                        }
                                    }
                                }

                                #endregion

                                #region Auto Select Network Security Group

                                if (asmVirtualMachine.NetworkSecurityGroup != null)
                                {
                                    foreach (TreeNode treeNode in treeSourceASM.Nodes.Find(asmVirtualMachine.NetworkSecurityGroup.Name, true))
                                    {
                                        if ((treeNode.Tag != null) && (treeNode.Tag.GetType() == typeof(Azure.MigrationTarget.NetworkSecurityGroup)))
                                        {
                                            if (!treeNode.Checked)
                                                treeNode.Checked = true;
                                        }
                                    }
                                }
                                #endregion
                            }

                            #endregion

                            #region OS Disk Storage Account

                            foreach (TreeNode treeNode in treeSourceASM.Nodes.Find(asmVirtualMachine.OSVirtualHardDisk.StorageAccountName, true))
                            {
                                if ((treeNode.Tag != null) && (treeNode.Tag.GetType() == typeof(Azure.MigrationTarget.StorageAccount)))
                                {
                                    if (!treeNode.Checked)
                                        treeNode.Checked = true;
                                }
                            }

                            #endregion

                            #region Data Disk(s) Storage Account(s)

                            foreach (Azure.Asm.Disk dataDisk in asmVirtualMachine.DataDisks)
                            {
                                foreach (TreeNode treeNode in treeSourceASM.Nodes.Find(dataDisk.StorageAccountName, true))
                                {
                                    if ((treeNode.Tag != null) && (treeNode.Tag.GetType() == typeof(Azure.MigrationTarget.StorageAccount)))
                                    {
                                        if (!treeNode.Checked)
                                            treeNode.Checked = true;
                                    }
                                }
                            }

                            #endregion
                        }

                        else if (targetVirtualMachine.Source.GetType() == typeof(Azure.Arm.VirtualMachine))
                        {
                            Azure.Arm.VirtualMachine armVirtualMachine = (Azure.Arm.VirtualMachine)targetVirtualMachine.Source;

                            #region process virtual network

                            foreach (Azure.Arm.NetworkInterface networkInterface in armVirtualMachine.NetworkInterfaces)
                            {
                                foreach (Azure.Arm.NetworkInterfaceIpConfiguration ipConfiguration in networkInterface.NetworkInterfaceIpConfigurations)
                                {
                                    if (ipConfiguration.VirtualNetwork != null)
                                    {
                                        foreach (TreeNode treeNode in treeSourceARM.Nodes.Find(ipConfiguration.VirtualNetwork.Name, true))
                                        {
                                            if ((treeNode.Tag != null) && (treeNode.Tag.GetType() == typeof(Azure.MigrationTarget.VirtualNetwork)))
                                            {
                                                if (!treeNode.Checked)
                                                    treeNode.Checked = true;
                                            }
                                        }
                                    }
                                }
                            }

                            #endregion

                            #region OS Disk Storage Account

                            if (armVirtualMachine.OSVirtualHardDisk.GetType() == typeof(Azure.Arm.Disk)) // Disk in a Storage Account, not a Managed Disk
                            { 
                                foreach (TreeNode treeNode in treeSourceARM.Nodes.Find(((Azure.Arm.Disk)armVirtualMachine.OSVirtualHardDisk).StorageAccountName, true))
                                {
                                    if ((treeNode.Tag != null) && (treeNode.Tag.GetType() == typeof(Azure.MigrationTarget.StorageAccount)))
                                    {
                                        if (!treeNode.Checked)
                                            treeNode.Checked = true;
                                    }
                                }
                            }

                            #endregion

                            #region Data Disk(s) Storage Account(s)

                            foreach (Azure.Arm.Disk dataDisk in armVirtualMachine.DataDisks)
                            {
                                foreach (TreeNode treeNode in treeSourceARM.Nodes.Find(dataDisk.StorageAccountName, true))
                                {
                                    if ((treeNode.Tag != null) && (treeNode.Tag.GetType() == typeof(Azure.MigrationTarget.StorageAccount)))
                                    {
                                        if (!treeNode.Checked)
                                            treeNode.Checked = true;
                                    }
                                }
                            }

                            #endregion
                        }
                    }
                }
                else if (selectedNode.Tag.GetType() == typeof(Azure.MigrationTarget.VirtualNetwork))
                {
                    Azure.MigrationTarget.VirtualNetwork targetVirtualNetwork = (Azure.MigrationTarget.VirtualNetwork)selectedNode.Tag;

                    foreach (Azure.MigrationTarget.Subnet targetSubnet in targetVirtualNetwork.TargetSubnets)
                    {
                        if (targetSubnet.NetworkSecurityGroup != null)
                        {
                            if (targetSubnet.NetworkSecurityGroup.SourceNetworkSecurityGroup != null)
                            {
                                if (targetSubnet.NetworkSecurityGroup.SourceNetworkSecurityGroup.GetType() == typeof(Azure.Asm.NetworkSecurityGroup))
                                {
                                    foreach (TreeNode treeNode in treeSourceASM.Nodes.Find(targetSubnet.NetworkSecurityGroup.SourceName, true))
                                    {
                                        if ((treeNode.Tag != null) && (treeNode.Tag.GetType() == typeof(Azure.MigrationTarget.NetworkSecurityGroup)))
                                        {
                                            if (!treeNode.Checked)
                                                treeNode.Checked = true;
                                        }
                                    }
                                }
                                else if (targetSubnet.NetworkSecurityGroup.SourceNetworkSecurityGroup.GetType() == typeof(Azure.Arm.NetworkSecurityGroup))
                                {
                                    foreach (TreeNode treeNode in treeSourceARM.Nodes.Find(targetSubnet.NetworkSecurityGroup.SourceName, true))
                                    {
                                        if ((treeNode.Tag != null) && (treeNode.Tag.GetType() == typeof(Azure.MigrationTarget.NetworkSecurityGroup)))
                                        {
                                            if (!treeNode.Checked)
                                                treeNode.Checked = true;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                
                StatusProvider.UpdateStatus("Ready");
            }
        }

        private List<TreeNode> GetSelectedNodes(TreeView treeView)
        {
            List<TreeNode> selectedNodes = new List<TreeNode>();
            foreach (TreeNode treeNode in treeView.Nodes)
            {
                RecursiveNodeSelectedAdd(ref selectedNodes, treeNode);
            }
            return selectedNodes;
        }

        private void RecursiveNodeSelectedAdd(ref List<TreeNode> selectedNodes, TreeNode parentNode)
        {
            if (parentNode.Checked && parentNode.Tag != null && 
                (parentNode.Tag.GetType() == typeof(Azure.MigrationTarget.NetworkSecurityGroup) || 
                parentNode.Tag.GetType() == typeof(Azure.MigrationTarget.VirtualNetwork) || 
                parentNode.Tag.GetType() == typeof(Azure.MigrationTarget.StorageAccount) ||
                parentNode.Tag.GetType() == typeof(Azure.MigrationTarget.VirtualMachine)
                ))
                selectedNodes.Add(parentNode);

            foreach (TreeNode childNode in parentNode.Nodes)
            {
                RecursiveNodeSelectedAdd(ref selectedNodes, childNode);
            }
        }
        #endregion

        #region ASM TreeView Events

        private void treeASM_AfterSelect(object sender, TreeViewEventArgs e)
        {
            _PropertyPanel.Clear();
        }

        private async void treeASM_AfterCheck(object sender, TreeViewEventArgs e)
        {
            if (_SourceAsmNode == null)
            {
                _SourceAsmNode = e.Node;
            }

            if (e.Node.Checked)
                await AutoSelectDependencies(e.Node);

            TreeNode resultUpdateARMTree = null;

            if (e.Node.Tag != null)
            { 
                Type tagType = e.Node.Tag.GetType();
                if ((tagType == typeof(Azure.MigrationTarget.VirtualNetwork)) ||
                    (tagType == typeof(Azure.MigrationTarget.StorageAccount)) ||
                    (tagType == typeof(Azure.MigrationTarget.VirtualMachine)) ||
                    (tagType == typeof(Azure.MigrationTarget.LoadBalancer)) ||
                    (tagType == typeof(Azure.MigrationTarget.PublicIp)) ||
                    (tagType == typeof(Azure.MigrationTarget.NetworkSecurityGroup)))
                {
                    if (e.Node.Checked)
                    {
                        resultUpdateARMTree = await AddMigrationTargetToTargetTree((MigAz.Core.Interface.IMigrationTarget)e.Node.Tag);
                    }
                    else
                    {
                        await RemoveASMNodeFromARMTree((MigAz.Core.Interface.IMigrationTarget)e.Node.Tag);
                    }
                }
            }

            if (_SourceAsmNode != null && _SourceAsmNode == e.Node)
            {
                if (e.Node.Checked)
                {
                    await RecursiveCheckToggleDown(e.Node, e.Node.Checked);
                    FillUpIfFullDown(e.Node);
                    treeSourceASM.SelectedNode = e.Node;
                }
                else
                {
                    await RecursiveCheckToggleUp(e.Node, e.Node.Checked);
                    await RecursiveCheckToggleDown(e.Node, e.Node.Checked);
                }

                _SelectedNodes = this.GetSelectedNodes(treeSourceASM);
                UpdateExportItemsCount();
                await this.TemplateGenerator.UpdateArtifacts(GetExportArtifacts());

                _SourceAsmNode = null;

                if (resultUpdateARMTree != null)
                    treeTargetARM.SelectedNode = resultUpdateARMTree;
            }
        }


        private void GetExportArtifactsRecursive(TreeNode parentTreeNode, ref ExportArtifacts exportArtifacts)
        {
            foreach (TreeNode selectedNode in parentTreeNode.Nodes)
            {
                Type tagType = selectedNode.Tag.GetType();

                if (tagType == typeof(Azure.MigrationTarget.VirtualNetwork))
                {
                    exportArtifacts.VirtualNetworks.Add((Azure.MigrationTarget.VirtualNetwork)selectedNode.Tag);
                }
                else if (tagType == typeof(Azure.MigrationTarget.StorageAccount))
                {
                    exportArtifacts.StorageAccounts.Add((Azure.MigrationTarget.StorageAccount)selectedNode.Tag);
                }
                else if (tagType == typeof(Azure.MigrationTarget.NetworkSecurityGroup))
                {
                    exportArtifacts.NetworkSecurityGroups.Add((Azure.MigrationTarget.NetworkSecurityGroup)selectedNode.Tag);
                }
                else if (tagType == typeof(Azure.MigrationTarget.VirtualMachine))
                {
                    exportArtifacts.VirtualMachines.Add((Azure.MigrationTarget.VirtualMachine)selectedNode.Tag);
                }
                else if (tagType == typeof(Azure.MigrationTarget.LoadBalancer))
                {
                    exportArtifacts.LoadBalancers.Add((Azure.MigrationTarget.LoadBalancer)selectedNode.Tag);
                }
            }

            foreach (TreeNode treeNode in parentTreeNode.Nodes)
            {
                GetExportArtifactsRecursive(treeNode, ref exportArtifacts);
            }
        }

        private ExportArtifacts GetExportArtifacts()
        {
            ExportArtifacts exportArtifacts = new ExportArtifacts();

            foreach (TreeNode treeNode in treeTargetARM.Nodes)
            {
                GetExportArtifactsRecursive(treeNode, ref exportArtifacts);
            }

            return exportArtifacts;
        }

        #endregion

        #region ARM TreeView Methods

        private async void treeARM_AfterSelect(object sender, TreeViewEventArgs e)
        {
            LogProvider.WriteLog("treeARM_AfterSelect", "Start");
            _SourceArmNode = e.Node;

            _PropertyPanel.Clear();
            _PropertyPanel.ResourceText = String.Empty;
            if (e.Node.Tag != null)
            {
                _PropertyPanel.ResourceText = e.Node.Text;

                if (e.Node.Tag.GetType() == typeof(Azure.MigrationTarget.VirtualMachine))
                {
                    this._PropertyPanel.ResourceImage = imageList1.Images["VirtualMachine"];

                    VirtualMachineProperties properties = new VirtualMachineProperties();
                    properties.LogProvider = LogProvider;
                    properties.AllowManangedDisk = false;
                    properties.PropertyChanged += Properties_PropertyChanged;
                    await properties.Bind(e.Node, this);
                    _PropertyPanel.PropertyDetailControl = properties;
                }
                else if (e.Node.Tag.GetType() == typeof(Azure.MigrationTarget.NetworkSecurityGroup))
                {
                    this._PropertyPanel.ResourceImage = imageList1.Images["NetworkSecurityGroup"];

                    NetworkSecurityGroupProperties properties = new NetworkSecurityGroupProperties();
                    properties.PropertyChanged += Properties_PropertyChanged;
                    properties.Bind(e.Node, this);
                    _PropertyPanel.PropertyDetailControl = properties;
                }
                if (e.Node.Tag.GetType() == typeof(Azure.MigrationTarget.VirtualNetwork))
                {
                    this._PropertyPanel.ResourceImage = imageList1.Images["VirtualNetwork"];

                    VirtualNetworkProperties properties = new VirtualNetworkProperties();
                    properties.PropertyChanged += Properties_PropertyChanged;
                    properties.Bind(e.Node);
                    _PropertyPanel.PropertyDetailControl = properties;
                }
                else if (e.Node.Tag.GetType() == typeof(Azure.MigrationTarget.Subnet))
                {
                    this._PropertyPanel.ResourceImage = imageList1.Images["VirtualNetwork"];

                    SubnetProperties properties = new SubnetProperties();
                    properties.PropertyChanged += Properties_PropertyChanged;
                    properties.Bind(e.Node);
                    _PropertyPanel.PropertyDetailControl = properties;
                }
                else if (e.Node.Tag.GetType() == typeof(Azure.MigrationTarget.StorageAccount))
                {
                    this._PropertyPanel.ResourceImage = imageList1.Images["StorageAccount"];

                    StorageAccountProperties properties = new StorageAccountProperties();
                    properties.PropertyChanged += Properties_PropertyChanged;
                    properties.Bind(this._AzureContextTargetARM, e.Node);
                    _PropertyPanel.PropertyDetailControl = properties;
                }
                else if (e.Node.Tag.GetType() == typeof(Azure.MigrationTarget.Disk))
                {
                    Azure.MigrationTarget.Disk migrationDisk = (Azure.MigrationTarget.Disk)e.Node.Tag;

                    this._PropertyPanel.ResourceImage = imageList1.Images["Disk"];

                    DiskProperties properties = new DiskProperties();
                    properties.LogProvider = this.LogProvider;
                    properties.AllowManangedDisk = false;
                    properties.PropertyChanged += Properties_PropertyChanged;
                    properties.Bind(this, e.Node);
                    _PropertyPanel.PropertyDetailControl = properties;
                }
                else if (e.Node.Tag.GetType() == typeof(Azure.MigrationTarget.AvailabilitySet))
                {
                    this._PropertyPanel.ResourceImage = imageList1.Images["AvailabilitySet"];

                    AvailabilitySetProperties properties = new AvailabilitySetProperties();
                    properties.PropertyChanged += Properties_PropertyChanged;
                    properties.Bind(e.Node);
                    _PropertyPanel.PropertyDetailControl = properties;
                }
                else if (e.Node.Tag.GetType() == typeof(Azure.MigrationTarget.NetworkInterface))
                {
                    this._PropertyPanel.ResourceImage = imageList1.Images["NetworkInterface"];

                    NetworkInterfaceProperties properties = new NetworkInterfaceProperties();
                    properties.PropertyChanged += Properties_PropertyChanged;
                    properties.Bind(this, e.Node);
                    _PropertyPanel.PropertyDetailControl = properties;
                }
                else if (e.Node.Tag.GetType() == typeof(Azure.MigrationTarget.ResourceGroup))
                {
                    this._PropertyPanel.ResourceImage = imageList1.Images["ResourceGroup"];

                    ResourceGroupProperties properties = new ResourceGroupProperties();
                    properties.PropertyChanged += Properties_PropertyChanged;
                    await properties.Bind(this, e.Node);
                    _PropertyPanel.PropertyDetailControl = properties;
                }
                else if (e.Node.Tag.GetType() == typeof(Azure.MigrationTarget.LoadBalancer))
                {
                    this._PropertyPanel.ResourceImage = imageList1.Images["LoadBalancer"];

                    LoadBalancerProperties properties = new LoadBalancerProperties();
                    properties.PropertyChanged += Properties_PropertyChanged;
                    await properties.Bind(this, e.Node);
                    _PropertyPanel.PropertyDetailControl = properties;
                }
                else if (e.Node.Tag.GetType() == typeof(Azure.MigrationTarget.PublicIp))
                {
                    this._PropertyPanel.ResourceImage = imageList1.Images["PublicIp"];

                    PublicIpProperties properties = new PublicIpProperties();
                    properties.PropertyChanged += Properties_PropertyChanged;
                    properties.Bind(e.Node);
                    _PropertyPanel.PropertyDetailControl = properties;
                }
            }

            _SourceArmNode = null;
            LogProvider.WriteLog("treeARM_AfterSelect", "End");
            StatusProvider.UpdateStatus("Ready");
        }

        private async Task Properties_PropertyChanged()
        {
            if (_SourceAsmNode == null && _SourceArmNode == null) // we are not going to update on every property bind during TreeView updates
                await this.TemplateGenerator.UpdateArtifacts(GetExportArtifacts());
        }

        private async Task RemoveASMNodeFromARMTree(IMigrationTarget migrationTarget)
        {

            TreeNode targetResourceGroupNode = SeekResourceGroupTreeNode();
            if (targetResourceGroupNode != null)
            {
                TreeNode[] matchingNodes = targetResourceGroupNode.Nodes.Find(migrationTarget.SourceName, true);
                foreach (TreeNode matchingNode in matchingNodes)
                {
                    if (matchingNode.Tag.GetType() == migrationTarget.GetType())
                        await RemoveTreeNodeCascadeUp(matchingNode);
                    else if (matchingNode.Tag.GetType() == typeof(Azure.MigrationTarget.StorageAccount))
                    {
                        if (migrationTarget.GetType() == typeof(Azure.Asm.StorageAccount))
                        {
                            await RemoveTreeNodeCascadeUp(matchingNode);
                        }
                        else if (migrationTarget.GetType() == typeof(Azure.Arm.StorageAccount))
                        {
                            await RemoveTreeNodeCascadeUp(matchingNode);
                        }
                    }
                    else if (matchingNode.Tag.GetType() == typeof(Azure.MigrationTarget.VirtualMachine))
                    {
                        if (migrationTarget.GetType() == typeof(Azure.Asm.VirtualMachine))
                        {
                            await RemoveTreeNodeCascadeUp(matchingNode);
                        }
                        else if (migrationTarget.GetType() == typeof(Azure.Arm.VirtualMachine))
                        {
                            await RemoveTreeNodeCascadeUp(matchingNode);
                        }
                    }
                    else if (matchingNode.Tag.GetType() == typeof(Azure.MigrationTarget.NetworkSecurityGroup))
                    {
                        if (migrationTarget.GetType() == typeof(Azure.Asm.NetworkSecurityGroup))
                        {
                            await RemoveTreeNodeCascadeUp(matchingNode);
                        }
                        else if (migrationTarget.GetType() == typeof(Azure.Arm.NetworkSecurityGroup))
                        {
                            await RemoveTreeNodeCascadeUp(matchingNode);
                        }
                    }
                    else if (matchingNode.Tag.GetType() == typeof(Azure.MigrationTarget.VirtualNetwork))
                    {
                        if (migrationTarget.GetType() == typeof(Azure.Asm.VirtualNetwork))
                        {
                            await RemoveTreeNodeCascadeUp(matchingNode);
                        }
                        else if (migrationTarget.GetType() == typeof(Azure.Arm.VirtualNetwork))
                        {
                            await RemoveTreeNodeCascadeUp(matchingNode);
                        }
                    }
                    else if (matchingNode.Tag.GetType() == typeof(TreeNode))
                    {
                        TreeNode childTreeNode = (TreeNode)matchingNode.Tag;
                        if (migrationTarget.GetType() == childTreeNode.Tag.GetType())
                            await RemoveTreeNodeCascadeUp(matchingNode);
                    }
                }
            }
        }

        private async Task RemoveTreeNodeCascadeUp(TreeNode treeNode)
        {
            TreeNode parentNode = treeNode.Parent;
            treeNode.Remove();
            await RemoveParentWhileNoChildren(parentNode);
        }

        private async Task RemoveParentWhileNoChildren(TreeNode treeNode)
        {
            if (treeNode != null)
            {
                if (treeNode.Nodes.Count == 0)
                {
                    TreeNode parentNode = treeNode.Parent;
                    treeNode.Remove();
                    await RemoveParentWhileNoChildren(parentNode);
                }
            }
        }

        internal TreeNode SeekARMChildTreeNode(string name, string text, object tag, bool allowCreated = false)
        {
            return SeekARMChildTreeNode(this.treeTargetARM.Nodes, name, text, tag, allowCreated);
        }

        internal TreeNode SeekARMChildTreeNode(TreeNodeCollection nodeCollection, string name, string text, object tag, bool allowCreated = false)
        {
            TreeNode[] childNodeMatch = nodeCollection.Find(name, false);

            foreach (TreeNode matchedNode in childNodeMatch)
            {
                if (matchedNode.Tag != null)
                {
                    if (matchedNode.Tag.GetType() == tag.GetType() && matchedNode.Text == text && matchedNode.Name == name)
                        return matchedNode;
                }
            }

            TreeNode childNode = null;
            if (allowCreated)
            {
                childNode = new TreeNode(text);
                childNode.Name = name;
                childNode.Tag = tag;
                if (tag.GetType() == typeof(Azure.MigrationTarget.ResourceGroup))
                {
                    childNode.ImageKey = "ResourceGroup";
                    childNode.SelectedImageKey = "ResourceGroup";
                }
                else if (tag.GetType() == typeof(Azure.MigrationTarget.StorageAccount))
                {
                    childNode.ImageKey = "StorageAccount";
                    childNode.SelectedImageKey = "StorageAccount";
                }
                else if (tag.GetType() == typeof(Azure.MigrationTarget.AvailabilitySet))
                {
                    childNode.ImageKey = "AvailabilitySet";
                    childNode.SelectedImageKey = "AvailabilitySet";
                }
                else if (tag.GetType() == typeof(Azure.MigrationTarget.VirtualMachine))
                {
                    childNode.ImageKey = "VirtualMachine";
                    childNode.SelectedImageKey = "VirtualMachine";
                }
                else if (tag.GetType() == typeof(Azure.MigrationTarget.VirtualNetwork))
                {
                    childNode.ImageKey = "VirtualNetwork";
                    childNode.SelectedImageKey = "VirtualNetwork";
                }
                else if (tag.GetType() == typeof(Azure.MigrationTarget.Subnet))
                {
                    childNode.ImageKey = "VirtualNetwork";
                    childNode.SelectedImageKey = "VirtualNetwork";
                }
                else if (tag.GetType() == typeof(Azure.MigrationTarget.NetworkSecurityGroup))
                {
                    childNode.ImageKey = "NetworkSecurityGroup";
                    childNode.SelectedImageKey = "NetworkSecurityGroup";
                }
                else if (tag.GetType() == typeof(Azure.MigrationTarget.Disk))
                {
                    childNode.ImageKey = "Disk";
                    childNode.SelectedImageKey = "Disk";
                }
                else if (tag.GetType() == typeof(Azure.MigrationTarget.NetworkInterface))
                {
                    childNode.ImageKey = "NetworkInterface";
                    childNode.SelectedImageKey = "NetworkInterface";
                }
                else if (tag.GetType() == typeof(Azure.MigrationTarget.LoadBalancer))
                {
                    childNode.ImageKey = "LoadBalancer";
                    childNode.SelectedImageKey = "LoadBalancer";
                }
                else if (tag.GetType() == typeof(Azure.MigrationTarget.PublicIp))
                {
                    childNode.ImageKey = "PublicIp";
                    childNode.SelectedImageKey = "PublicIp";
                }
                else
                    throw new ArgumentException("Unknown node tag type: " + tag.GetType().ToString());

                nodeCollection.Add(childNode);
                childNode.ExpandAll();
                return childNode;
            }
            return null;
        }

        private TreeNode GetResourceGroupTreeNode(TreeNode subscriptionNode, ResourceGroup resourceGroup)
        {
            foreach (TreeNode treeNode in subscriptionNode.Nodes)
            {
                if (treeNode.Tag != null)
                {
                    if (treeNode.Tag.GetType() == resourceGroup.GetType() && treeNode.Text == resourceGroup.ToString())
                        return treeNode;
                }
            }

            TreeNode tnResourceGroup = new TreeNode(resourceGroup.ToString());
            tnResourceGroup.Text = resourceGroup.ToString();
            tnResourceGroup.Tag = resourceGroup;
            tnResourceGroup.ImageKey = "ResourceGroup";
            tnResourceGroup.SelectedImageKey = "ResourceGroup";

            subscriptionNode.Nodes.Add(tnResourceGroup);
            tnResourceGroup.Expand();
            return tnResourceGroup;
        }

        private TreeNode GetAvailabilitySetTreeNode(TreeNode subscriptionNode, Azure.MigrationTarget.AvailabilitySet availabilitySet)
        {
            foreach (TreeNode treeNode in subscriptionNode.Nodes)
            {
                if (treeNode.Tag != null)
                {
                    if (treeNode.Tag.GetType() == availabilitySet.GetType() && treeNode.Text == availabilitySet.ToString())
                        return treeNode;
                }
            }

            TreeNode tnAvailabilitySet = new TreeNode(availabilitySet.ToString());
            tnAvailabilitySet.Text = availabilitySet.ToString();
            tnAvailabilitySet.Tag = availabilitySet;
            tnAvailabilitySet.ImageKey = "AvailabilitySet";
            tnAvailabilitySet.SelectedImageKey = "AvailabilitySet";

            subscriptionNode.Nodes.Add(tnAvailabilitySet);
            tnAvailabilitySet.Expand();
            return tnAvailabilitySet;
        }

        private TreeNode GetTargetAvailabilitySetNode(TreeNode subscriptionNode, Azure.MigrationTarget.AvailabilitySet targetAvailabilitySet)
        {
            foreach (TreeNode treeNode in subscriptionNode.Nodes)
            {
                if (treeNode.Tag != null)
                {
                    if (treeNode.Tag.GetType() == typeof(Azure.MigrationTarget.AvailabilitySet) && treeNode.Text == targetAvailabilitySet.ToString())
                        return treeNode;
                }
            }

            TreeNode tnAvailabilitySet = new TreeNode(targetAvailabilitySet.ToString());
            tnAvailabilitySet.Text = targetAvailabilitySet.ToString();
            tnAvailabilitySet.Tag = targetAvailabilitySet;
            tnAvailabilitySet.ImageKey = "AvailabilitySet";
            tnAvailabilitySet.SelectedImageKey = "AvailabilitySet";

            subscriptionNode.Nodes.Add(tnAvailabilitySet);
            tnAvailabilitySet.Expand();
            return tnAvailabilitySet;
        }
        private TreeNode GetAvailabilitySetNode(TreeNode subscriptionNode, Azure.Asm.VirtualMachine virtualMachine)
        {
            string availabilitySetName = String.Empty;

            if (virtualMachine.AvailabilitySetName != String.Empty)
                availabilitySetName = virtualMachine.AvailabilitySetName;
            else
                availabilitySetName = virtualMachine.CloudServiceName;

            foreach (TreeNode treeNode in subscriptionNode.Nodes)
            {
                if (treeNode.Tag != null)
                {
                    if (treeNode.Tag.GetType() == typeof(Azure.MigrationTarget.AvailabilitySet) && treeNode.Text == availabilitySetName)
                        return treeNode;
                }
            }

            TreeNode tnAvailabilitySet = new TreeNode(availabilitySetName);
            tnAvailabilitySet.Text = availabilitySetName;
            tnAvailabilitySet.Tag = new Azure.MigrationTarget.AvailabilitySet(this.AzureContextTargetARM, availabilitySetName);
            tnAvailabilitySet.ImageKey = "AvailabilitySet";
            tnAvailabilitySet.SelectedImageKey = "AvailabilitySet";

            subscriptionNode.Nodes.Add(tnAvailabilitySet);
            tnAvailabilitySet.Expand();
            return tnAvailabilitySet;
        }

        private TreeNode GetDataCenterTreeViewNode(TreeNode subscriptionNode, string dataCenter, string containerName)
        {
            TreeNode dataCenterNode = null;

            foreach (TreeNode treeNode in subscriptionNode.Nodes)
            {
                if (treeNode.Text == dataCenter && treeNode.Tag.ToString() == "DataCenter")
                {
                    dataCenterNode = treeNode;

                    foreach (TreeNode dataCenterContainerNode in treeNode.Nodes)
                    {
                        if (dataCenterContainerNode.Text == containerName)
                            return dataCenterContainerNode;
                    }
                }
            }

            if (dataCenterNode == null)
            {
                dataCenterNode = new TreeNode(dataCenter);
                dataCenterNode.Tag = "DataCenter";
                subscriptionNode.Nodes.Add(dataCenterNode);
                dataCenterNode.Expand();
            }

            TreeNode containerNode = new TreeNode(containerName);
            dataCenterNode.Nodes.Add(containerNode);
            containerNode.Expand();

            return containerNode;
        }

        private void FillUpIfFullDown(TreeNode node)
        {
            if (IsSelectedFullDown(node) && (node.Parent != null))
            {
                node = node.Parent;

                while (node != null)
                {
                    if (AllChildrenChecked(node))
                    {
                        node.Checked = true;
                        node = node.Parent;
                    }
                    else
                        node = null;
                }
            }
        }

        private bool AllChildrenChecked(TreeNode node)
        {
            foreach (TreeNode childNode in node.Nodes)
                if (!childNode.Checked)
                    return false;

            return true;
        }

        private bool IsSelectedFullDown(TreeNode node)
        {
            if (!node.Checked)
                return false;

            foreach (TreeNode childNode in node.Nodes)
            {
                if (!IsSelectedFullDown(childNode))
                    return false;
            }

            return true;
        }

        private async Task RecursiveCheckToggleDown(TreeNode node, bool isChecked)
        {
            if (node.Checked != isChecked)
            {
                node.Checked = isChecked;
            }

            foreach (TreeNode subNode in node.Nodes)
            {
                await RecursiveCheckToggleDown(subNode, isChecked);
            }
        }
        private async Task RecursiveCheckToggleUp(TreeNode node, bool isChecked)
        {
            if (node.Checked != isChecked)
            {
                node.Checked = isChecked;
            }

            if (node.Parent != null)
                await RecursiveCheckToggleUp(node.Parent, isChecked);
        }

        private async Task<TreeNode> AddMigrationTargetToTargetTree(IMigrationTarget parentNode)
        {
            if (parentNode == null)
                throw new ArgumentNullException("Migration Target cannot be null.");

            TreeNode targetResourceGroupNode = SeekResourceGroupTreeNode();

            if (parentNode.GetType() == typeof(Azure.MigrationTarget.VirtualNetwork))
            {
                Azure.MigrationTarget.VirtualNetwork targetVirtualNetwork = (Azure.MigrationTarget.VirtualNetwork)parentNode;
                TreeNode virtualNetworkNode = SeekARMChildTreeNode(targetResourceGroupNode.Nodes, targetVirtualNetwork.SourceName, targetVirtualNetwork.ToString(), targetVirtualNetwork, true);

                foreach (Azure.MigrationTarget.Subnet targetSubnet in targetVirtualNetwork.TargetSubnets)
                {
                    TreeNode subnetNode = SeekARMChildTreeNode(virtualNetworkNode.Nodes, targetVirtualNetwork.ToString(), targetSubnet.ToString(), targetSubnet, true);
                }

                targetResourceGroupNode.ExpandAll();
                return virtualNetworkNode;
            }
            else if (parentNode.GetType() == typeof(Azure.MigrationTarget.StorageAccount))
            {
                Azure.MigrationTarget.StorageAccount targetStorageAccount = (Azure.MigrationTarget.StorageAccount)parentNode;

                TreeNode storageAccountNode = SeekARMChildTreeNode(targetResourceGroupNode.Nodes, targetStorageAccount.SourceName, targetStorageAccount.ToString(), targetStorageAccount, true);

                targetResourceGroupNode.ExpandAll();
                return storageAccountNode;
            }
            else if (parentNode.GetType() == typeof(Azure.MigrationTarget.NetworkSecurityGroup))
            {
                Azure.MigrationTarget.NetworkSecurityGroup targetNetworkSecurityGroup = (Azure.MigrationTarget.NetworkSecurityGroup)parentNode;
                TreeNode networkSecurityGroupNode = SeekARMChildTreeNode(targetResourceGroupNode.Nodes, targetNetworkSecurityGroup.SourceName, targetNetworkSecurityGroup.ToString(), targetNetworkSecurityGroup, true);

                targetResourceGroupNode.ExpandAll();
                return networkSecurityGroupNode;
            }
            else if (parentNode.GetType() == typeof(Azure.MigrationTarget.LoadBalancer))
            {
                Azure.MigrationTarget.LoadBalancer targetLoadBalancer = (Azure.MigrationTarget.LoadBalancer)parentNode;
                TreeNode targetLoadBalancerNode = SeekARMChildTreeNode(targetResourceGroupNode.Nodes, targetLoadBalancer.SourceName, targetLoadBalancer.ToString(), targetLoadBalancer, true);

                targetResourceGroupNode.ExpandAll();
                return targetLoadBalancerNode;
            }
            else if (parentNode.GetType() == typeof(Azure.MigrationTarget.PublicIp))
            {
                Azure.MigrationTarget.PublicIp targetPublicIp = (Azure.MigrationTarget.PublicIp)parentNode;
                TreeNode targetPublicIpNode = SeekARMChildTreeNode(targetResourceGroupNode.Nodes, targetPublicIp.SourceName, targetPublicIp.ToString(), targetPublicIp, true);

                targetResourceGroupNode.ExpandAll();
                return targetPublicIpNode;
            }
            else if (parentNode.GetType() == typeof(Azure.MigrationTarget.VirtualMachine))
            {
                Azure.MigrationTarget.VirtualMachine targetVirtualMachine = (Azure.MigrationTarget.VirtualMachine)parentNode;

                TreeNode virtualMachineParentNode = targetResourceGroupNode;
                TreeNode targetAvailabilitySetNode = null;

                // https://docs.microsoft.com/en-us/azure/virtual-machines/windows/manage-availability
                if (targetVirtualMachine.TargetAvailabilitySet != null)
                {
                    targetAvailabilitySetNode = GetTargetAvailabilitySetNode(targetResourceGroupNode, targetVirtualMachine.TargetAvailabilitySet);
                    virtualMachineParentNode = targetAvailabilitySetNode;
                }

                TreeNode virtualMachineNode = SeekARMChildTreeNode(virtualMachineParentNode.Nodes, targetVirtualMachine.SourceName, targetVirtualMachine.ToString(), targetVirtualMachine, true);

                foreach (Azure.MigrationTarget.Disk targetDisk in targetVirtualMachine.DataDisks)
                {
                    TreeNode dataDiskNode = SeekARMChildTreeNode(virtualMachineNode.Nodes, targetDisk.ToString(), targetDisk.ToString(), targetDisk, true);
                }

                foreach (Azure.MigrationTarget.NetworkInterface targetNetworkInterface in targetVirtualMachine.NetworkInterfaces)
                {
                    TreeNode networkInterfaceNode = SeekARMChildTreeNode(virtualMachineNode.Nodes, targetNetworkInterface.ToString(), targetNetworkInterface.ToString(), targetNetworkInterface, true);
                }

                targetResourceGroupNode.ExpandAll();
                return virtualMachineNode;
            }
            else
                throw new Exception("Unhandled Node Type in AddMigrationTargetToTargetTree: " + parentNode.GetType());

        }

        private TreeNode SeekResourceGroupTreeNode()
        {
            TreeNode targetResourceGroupNode = SeekARMChildTreeNode(treeTargetARM.Nodes, _TargetResourceGroup.ToString(), _TargetResourceGroup.ToString(), _TargetResourceGroup, true);
            return targetResourceGroupNode;
        }

        #endregion

        #region Form Controls

        #region Export Button

        public async void Export()
        {
            await SaveSubscriptionSettings(_AzureContextSourceASM.AzureSubscription);
        }

        #endregion

        #endregion

        #region Form Events

        private async void AsmToArmForm_Load(object sender, EventArgs e)
        {
            LogProvider.WriteLog("AsmToArmForm_Load", "Program start");

            AsmToArmForm_Resize(null, null);
            ResetForm();

            try
            {
                _AzureContextSourceASM.AzureEnvironment = (AzureEnvironment)Enum.Parse(typeof(AzureEnvironment), app.Default.AzureEnvironment);
            }
            catch
            {
                _AzureContextSourceASM.AzureEnvironment = AzureEnvironment.AzureCloud;
            }

            await AlertIfNewVersionAvailable(); // check if there a new version of the app
        }

        private void AsmToArmForm_Resize(object sender, EventArgs e)
        {
            tabSourceResources.Height = this.Height - 130;
            treeSourceASM.Width = tabSourceResources.Width - 8;
            treeSourceASM.Height = tabSourceResources.Height - 26;
            treeSourceARM.Width = tabSourceResources.Width - 8;
            treeSourceARM.Height = tabSourceResources.Height - 26;
            treeTargetARM.Height = this.Height - 150;
        }

        #endregion

        #region Subscription Settings Read / Save Methods

        private async Task SaveSubscriptionSettings(AzureSubscription azureSubscription)
        {
            // If save selection option is enabled
            if (app.Default.SaveSelection && azureSubscription != null)
            {
                await _saveSelectionProvider.Save(azureSubscription.SubscriptionId, _SelectedNodes);
            }
        }

        private async Task ReadSubscriptionSettings(AzureSubscription azureSubscription)
        {
            // If save selection option is enabled
            if (app.Default.SaveSelection)
            {
                StatusProvider.UpdateStatus("BUSY: Reading saved selection");
                await _saveSelectionProvider.Read(azureSubscription.SubscriptionId, _AzureContextSourceASM.AzureRetriever, AzureContextTargetARM.AzureRetriever, treeSourceASM);
                UpdateExportItemsCount();
            }
        }

        #endregion

        private void UpdateExportItemsCount()
        {
            Int32 selectedExportCount = 0;

            if (_SelectedNodes != null)
            {
                selectedExportCount = _SelectedNodes.Count();
            }
        }

        private void SeekAlertSourceRecursive(object sourceObject, TreeNodeCollection nodes)
        {
            foreach (TreeNode treeNode in nodes)
            {
                if (treeNode.Tag != null)
                {
                    object nodeObject = null;

                    if (treeNode.Tag.GetType() == typeof(TreeNode))
                    {
                        TreeNode asmTreeNode = (TreeNode)treeNode.Tag;
                        nodeObject = asmTreeNode.Tag;
                    }
                    else
                    {
                        nodeObject = treeNode.Tag;
                    }

                    // Note, this could probably be object compares, but was written this was to get it done.  Possible future change to object compares
                    if (nodeObject.GetType() == sourceObject.GetType())
                    {
                        if (sourceObject.GetType() == typeof(Azure.MigrationTarget.ResourceGroup))
                            treeTargetARM.SelectedNode = treeNode;
                        else if (sourceObject.GetType() == typeof(Azure.MigrationTarget.VirtualMachine) && sourceObject.ToString() == nodeObject.ToString())
                            treeTargetARM.SelectedNode = treeNode;
                        else if (sourceObject.GetType() == typeof(Azure.MigrationTarget.Disk) && sourceObject.ToString() == nodeObject.ToString())
                            treeTargetARM.SelectedNode = treeNode;
                        else if (sourceObject.GetType() == typeof(Azure.MigrationTarget.NetworkInterface) && sourceObject.ToString() == nodeObject.ToString())
                            treeTargetARM.SelectedNode = treeNode;
                    }
                }
                SeekAlertSourceRecursive(sourceObject, treeNode.Nodes);
            }
        }

        public override void SeekAlertSource(object sourceObject)
        {
            SeekAlertSourceRecursive(sourceObject, treeTargetARM.Nodes);
        }

        public override void PostTelemetryRecord()
        {
            _telemetryProvider.PostTelemetryRecord((AzureGenerator) this.TemplateGenerator);
        }
    }
}
