// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SqlServer.TDS.PreLogin;
using Xunit;

namespace Microsoft.Data.SqlClient.Tests
{
    public class SqlConnectionEncryptionTests
    {
        [Fact]
        public void ConnectionTest()
        {
            //AppContext.SetSwitch("Switch.Microsoft.Data.SqlClient.UseManagedNetworkingOnWindows", true);
            //using TestTdsServer server = TestTdsServer.StartTestServer(false, false, 10, "",
            //    null, encryptionType: TDSPreLoginTokenEncryptionType.On);
            using TestTdsServer server = TestTdsServer.StartTestServer(false, false, 5, "",
                new X509Certificate2("localhost123_server.pfx", "SecretPlaceholder123456", X509KeyStorageFlags.UserKeySet), encryptionType: TDSPreLoginTokenEncryptionType.On);
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(server.ConnectionString);
            builder.Encrypt = SqlConnectionEncryptOption.Mandatory;
            using SqlConnection connection = new(builder.ConnectionString);
            connection.Open();
        }

    }
}
