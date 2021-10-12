using Microsoft.Azure.Management.Network;
using Microsoft.Azure.Management.Network.Models;
using NUnit.Framework;
using System;
using System.Linq;

namespace AzureGraderTestProject
{
    public class VnetTests
    {
        private NetworkManagementClient _client;
        private VirtualNetwork _vnet;
        [SetUp]
        public void Setup()
        {
            var config = new Config();
#pragma warning disable IDE0017 // Simplify object initialization
            _client = new NetworkManagementClient(config.Credentials);
#pragma warning restore IDE0017 // Simplify object initialization
            _client.SubscriptionId = config.SubscriptionId;
            _vnet = _client.VirtualNetworks.Get("IT114115", "VNet1");
        }

        [TearDown]
        public void TearDown()
        {
            _client.Dispose();
        }

        [Test]
        public void Test01_HasVnet()
        {
            Assert.DoesNotThrow(() => { Console.WriteLine(_vnet.Name); }, "VNET in resource group IT114115 with name VNET1");
        }

        [Test]
        public void Test02_AddressSpace()
        {
            Assert.AreEqual("10.0.0.0/16", _vnet.AddressSpace.AddressPrefixes[0], "Address space 10.0.0.0/16");
        }

        [Test]
        public void Test03_2Subnets()
        {
            Assert.AreEqual(2, _vnet.Subnets.Count, "2 subnets");
        }

        [Test]
        public void Test04_2SubnetsCidr()
        {
            var subnet1 = _vnet.Subnets.FirstOrDefault(c => c.AddressPrefix == "10.0.0.0/24");
            var subnet2 = _vnet.Subnets.FirstOrDefault(c => c.AddressPrefix == "10.0.1.0/24");
            Assert.IsNotNull(subnet1);
            Assert.IsNotNull(subnet2);
        }
    }
}