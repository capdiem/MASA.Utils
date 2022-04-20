namespace Masa.Utils.Ldap.Novell;

public class LdapProvider : ILdapProvider, IDisposable
{
    ILdapConnection ldapConnection = null!;
    LdapOptions _ldapOptions;

    private readonly string[] _attributes =
    {
        "objectSid", "objectGUID", "objectCategory", "objectClass", "memberOf", "name", "cn", "distinguishedName",
        "sAMAccountName", "userPrincipalName", "displayName", "givenName", "sn", "description",
        "telephoneNumber", "mail", "streetAddress", "postalCode", "l", "st", "co", "c"
    };

    internal LdapProvider(LdapOptions options)
    {
        _ldapOptions = options;
    }

    public LdapProvider(IOptionsSnapshot<LdapOptions> options)
    {
        _ldapOptions = options.Value;
    }

    private async Task<ILdapConnection> GetConnectionAsync()
    {
        if (ldapConnection != null && ldapConnection.Connected)
        {
            return ldapConnection;
        }
        ldapConnection = new LdapConnection() { SecureSocketLayer = _ldapOptions.ServerPortSsl != 0 };
        //Connect function will create a socket connection to the server - Port 389 for insecure and 3269 for secure    
        await ldapConnection.ConnectAsync(_ldapOptions.ServerAddress,
            _ldapOptions.ServerPortSsl != 0 ? _ldapOptions.ServerPortSsl : _ldapOptions.ServerPort);
        //Bind function with null user dn and password value will perform anonymous bind to LDAP server 
        await ldapConnection.BindAsync(_ldapOptions.RootUserDn, _ldapOptions.RootUserPassword);

        return ldapConnection;
    }

    public async Task<bool> AuthenticateAsync(string distinguishedName, string password)
    {
        using var ldapConnection = new LdapConnection() { SecureSocketLayer = _ldapOptions.ServerPortSsl != 0 };
        await ldapConnection.ConnectAsync(_ldapOptions.ServerAddress,
            ldapConnection.SecureSocketLayer ? _ldapOptions.ServerPortSsl : _ldapOptions.ServerPort);
        try
        {
            await ldapConnection.BindAsync(distinguishedName, password);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task DeleteUserAsync(string distinguishedName)
    {
        using (var ldapConnection = await GetConnectionAsync())
        {
            await ldapConnection.DeleteAsync(distinguishedName);
        }
    }

    public async Task AddUserAsync(LdapUser user, string password)
    {
        var dn = $"CN={user.FirstName} {user.LastName},{_ldapOptions.UserSearchBaseDn}";

        var attributeSet = new LdapAttributeSet
        {
            new LdapAttribute("instanceType", "4"),
            new LdapAttribute("objectCategory", $"CN=Users,{_ldapOptions.UserSearchBaseDn}"),
            new LdapAttribute("objectClass", new[] {"top", "person", "organizationalPerson", "user"}),
            new LdapAttribute("name", user.Name),
            new LdapAttribute("cn", $"{user.FirstName} {user.LastName}"),
            new LdapAttribute("sAMAccountName", user.SamAccountName),
            new LdapAttribute("userPrincipalName", user.UserPrincipalName),
            new LdapAttribute("unicodePwd", Convert.ToBase64String(Encoding.Unicode.GetBytes($"\"{password}\""))),
            new LdapAttribute("userAccountControl", "512"),
            new LdapAttribute("givenName", user.FirstName),
            new LdapAttribute("sn", user.LastName),
            new LdapAttribute("mail", user.EmailAddress)
        };

        attributeSet.AddAttribute("displayName", user.DisplayName);
        attributeSet.AddAttribute("description", user.Description);
        attributeSet.AddAttribute("telephoneNumber", user.Phone);
        attributeSet.AddAttribute("streetAddress", user.Address.Street);
        attributeSet.AddAttribute("l", user.Address.City);
        attributeSet.AddAttribute("postalCode", user.Address.PostalCode);
        attributeSet.AddAttribute("st", user.Address.StateName);
        attributeSet.AddAttribute("co", user.Address.CountryName);
        attributeSet.AddAttribute("c", user.Address.CountryCode);

        var newEntry = new LdapEntry(dn, attributeSet);

        using var ldapConnection = await GetConnectionAsync();
        await ldapConnection.AddAsync(newEntry);
    }

    public async IAsyncEnumerable<LdapUser> GetAllUserAsync()
    {
        var filter = $"(&(objectCategory=person)(objectClass=user))";
        var users = GetFilterLdapEntryAsync(_ldapOptions.UserSearchBaseDn, filter);
        await foreach (var user in users)
        {
            yield return CreateUser(user.Dn, user.GetAttributeSet());
        }
    }

    public async Task<List<LdapEntry>> GetPagingUserAsync(int pageSize)
    {
        using var ldapConnection = await GetConnectionAsync();
        return await ldapConnection.SearchUsingSimplePagingAsync(new SearchOptions(
            _ldapOptions.UserSearchBaseDn,
            LdapConnection.ScopeSub,
            "(&(objectCategory=person)(objectClass=user))",
            _attributes),
            pageSize);
    }

    public async Task<LdapUser?> GetUserByUserNameAsync(string userName)
    {
        var filter = $"(&(objectClass=user)(sAMAccountName={userName}))";
        var user = await GetFilterLdapEntryAsync(_ldapOptions.UserSearchBaseDn, filter).FirstOrDefaultAsync();
        return user == null ? null : CreateUser(user.Dn, user.GetAttributeSet());
    }

    public async Task<LdapUser?> GetUsersByEmailAddressAsync(string emailAddress)
    {
        var filter = $"(&(objectClass=user)(mail={emailAddress}))";
        var user = await GetFilterLdapEntryAsync(_ldapOptions.UserSearchBaseDn, filter).FirstOrDefaultAsync();
        return user == null ? null : CreateUser(user.Dn, user.GetAttributeSet());
    }

    private async IAsyncEnumerable<LdapEntry> GetFilterLdapEntryAsync(string baseDn, string filter)
    {
        using var ldapConnection = await GetConnectionAsync();
        var searchResults = await ldapConnection.SearchAsync(
                baseDn,
                LdapConnection.ScopeSub,
                filter,
                _attributes,
                false);
        await foreach (var searchResult in searchResults)
        {
            yield return searchResult;
        }
    }

    public async IAsyncEnumerable<LdapUser> GetUsersInGroupAsync(string groupName)
    {
        var group = await GetGroupAsync(groupName);
        if (group == null)
        {
            yield break;
        }
        var filter = $"(&(objectCategory=person)(objectClass=user)(memberOf={group.Dn}))";
        var users = GetFilterLdapEntryAsync(_ldapOptions.UserSearchBaseDn, filter);

        await foreach (var user in users)
        {
            yield return CreateUser(user.Dn, user.GetAttributeSet());
        }
    }

    public async Task<LdapEntry?> GetGroupAsync(string groupName)
    {
        var filter = $"(&(objectCategory=group)(objectClass=group)(cn={groupName}))";
        return await GetFilterLdapEntryAsync(_ldapOptions.GroupSearchBaseDn, filter)
            .FirstOrDefaultAsync();
    }

    public void Dispose()
    {
        if (ldapConnection.Connected)
        {
            ldapConnection.Disconnect();
        }
        if (ldapConnection != null)
        {
            ldapConnection.Dispose();
        }
    }

    private LdapUser CreateUser(string distinguishedName, LdapAttributeSet attributeSet)
    {
        var ldapUser = new LdapUser();

        ldapUser.ObjectSid = attributeSet.GetString("objectSid");
        ldapUser.ObjectGuid = attributeSet.GetString("objectGUID");
        ldapUser.ObjectCategory = attributeSet.GetString("objectCategory");
        ldapUser.ObjectClass = attributeSet.GetString("objectClass");
        ldapUser.MemberOf = attributeSet.GetStringArray("memberOf");
        ldapUser.CommonName = attributeSet.GetString("cn");
        ldapUser.SamAccountName = attributeSet.GetString("sAMAccountName");
        ldapUser.UserPrincipalName = attributeSet.GetString("userPrincipalName");
        ldapUser.Name = attributeSet.GetString("name");
        ldapUser.DistinguishedName = attributeSet.GetString("distinguishedName");
        ldapUser.DisplayName = attributeSet.GetString("displayName");
        ldapUser.FirstName = attributeSet.GetString("givenName");
        ldapUser.LastName = attributeSet.GetString("sn");
        ldapUser.Description = attributeSet.GetString("description");
        ldapUser.Phone = attributeSet.GetString("telephoneNumber");
        ldapUser.EmailAddress = attributeSet.GetString("mail");
        ldapUser.Address = new LdapAddress
        {
            Street = attributeSet.GetString("streetAddress"),
            City = attributeSet.GetString("l"),
            PostalCode = attributeSet.GetString("postalCode"),
            StateName = attributeSet.GetString("st"),
            CountryName = attributeSet.GetString("co"),
            CountryCode = attributeSet.GetString("c")
        };
        attributeSet.TryGetValue("sAMAccountType", out var sAMAccountType);
        ldapUser.SamAccountType = int.Parse(sAMAccountType?.StringValue ?? "0");

        ldapUser.IsDomainAdmin = ldapUser.MemberOf.Contains("CN=Domain Admins," + _ldapOptions.BaseDn);
        return ldapUser;
    }
}
