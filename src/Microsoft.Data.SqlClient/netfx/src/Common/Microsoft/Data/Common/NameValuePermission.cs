// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.Data.Common
{

    using System;
    using System.Collections;
    using System.Diagnostics;

    [Serializable] // MDAC 83147
    sealed internal class NameValuePermission : IComparable
    {
        // reused as both key and value nodes
        // key nodes link to value nodes
        // value nodes link to key nodes
        private string _value;

        // value node with (_restrictions != null) are allowed to match connection strings
        private DBConnectionString _entry;

        private NameValuePermission[] _tree; // with branches

        static internal readonly NameValuePermission Default = null;// = new NameValuePermission(String.Empty, new string[] { "File Name" }, KeyRestrictionBehavior.AllowOnly);

        internal NameValuePermission()
        { // root node
        }

        private NameValuePermission(string keyword)
        {
            _value = keyword;
        }

        private NameValuePermission(string value, DBConnectionString entry)
        {
            _value = value;
            _entry = entry;
        }

        private NameValuePermission(NameValuePermission permit)
        { // deep-copy
            _value = permit._value;
            _entry = permit._entry;
            _tree = permit._tree;
            if (_tree != null)
            {
                NameValuePermission[] tree = (_tree.Clone() as NameValuePermission[]);
                for (int i = 0; i < tree.Length; ++i)
                {
                    if (tree[i] != null)
                    { // WebData 98488
                        tree[i] = tree[i].CopyNameValue(); // deep copy
                    }
                }
                _tree = tree;
            }
        }

        int IComparable.CompareTo(object a)
        {
            return StringComparer.Ordinal.Compare(_value, ((NameValuePermission)a)._value);
        }

        static internal void AddEntry(NameValuePermission kvtree, ArrayList entries, DBConnectionString entry)
        {
            Debug.Assert(entry != null, "null DBConnectionString");

            if (entry.KeyChain != null)
            {
                for (NameValuePair keychain = entry.KeyChain; keychain != null; keychain = keychain.Next)
                {
                    NameValuePermission kv;

                    kv = kvtree.CheckKeyForValue(keychain.Name);
                    if (kv == null)
                    {
                        kv = new NameValuePermission(keychain.Name);
                        kvtree.Add(kv); // add directly into live tree
                    }
                    kvtree = kv;

                    kv = kvtree.CheckKeyForValue(keychain.Value);
                    if (kv == null)
                    {
                        DBConnectionString insertValue = keychain.Next != null ? null : entry;
                        kv = new NameValuePermission(keychain.Value, insertValue);
                        kvtree.Add(kv); // add directly into live tree
                        if (insertValue != null)
                        {
                            entries.Add(insertValue);
                        }
                    }
                    else if (keychain.Next == null)
                    { // shorter chain potential
                        if (kv._entry != null)
                        {
                            Debug.Assert(entries.Contains(kv._entry), "entries doesn't contain entry");
                            entries.Remove(kv._entry);
                            kv._entry = kv._entry.Intersect(entry); // union new restrictions into existing tree
                        }
                        else
                        {
                            kv._entry = entry;
                        }
                        entries.Add(kv._entry);
                    }
                    kvtree = kv;
                }
            }
            else
            { // global restrictions, MDAC 84443
                DBConnectionString kentry = kvtree._entry;
                if (kentry != null)
                {
                    Debug.Assert(entries.Contains(kentry), "entries doesn't contain entry");
                    entries.Remove(kentry);
                    kvtree._entry = kentry.Intersect(entry);
                }
                else
                {
                    kvtree._entry = entry;
                }
                entries.Add(kvtree._entry);
            }
        }

        internal void Intersect(ArrayList entries, NameValuePermission target)
        {
            if (target == null)
            {
                _tree = null;
                _entry = null;
            }
            else
            {
                if (_entry != null)
                {
                    entries.Remove(_entry);
                    _entry = _entry.Intersect(target._entry);
                    entries.Add(_entry);
                }
                else if (target._entry != null)
                {
                    _entry = target._entry.Intersect(null);
                    entries.Add(_entry);
                }

                if (_tree != null)
                {
                    int count = _tree.Length;
                    for (int i = 0; i < _tree.Length; ++i)
                    {
                        NameValuePermission kvtree = target.CheckKeyForValue(_tree[i]._value);
                        if (kvtree != null)
                        { // does target tree contain our value
                            _tree[i].Intersect(entries, kvtree);
                        }
                        else
                        {
                            _tree[i] = null;
                            --count;
                        }
                    }
                    if (0 == count)
                    {
                        _tree = null;
                    }
                    else if (count < _tree.Length)
                    {
                        NameValuePermission[] kvtree = new NameValuePermission[count];
                        for (int i = 0, j = 0; i < _tree.Length; ++i)
                        {
                            if (_tree[i] != null)
                            {
                                kvtree[j++] = _tree[i];
                            }
                        }
                        _tree = kvtree;
                    }
                }
            }
        }

        private void Add(NameValuePermission permit)
        {
            NameValuePermission[] tree = _tree;
            int length = tree != null ? tree.Length : 0;
            NameValuePermission[] newtree = new NameValuePermission[1 + length];
            for (int i = 0; i < newtree.Length - 1; ++i)
            {
                newtree[i] = tree[i];
            }
            newtree[length] = permit;
            Array.Sort(newtree);
            _tree = newtree;
        }

        internal bool CheckValueForKeyPermit(DBConnectionString parsetable)
        {
            if (parsetable == null)
            {
                return false;
            }
            bool hasMatch = false;
            NameValuePermission[] keytree = _tree; // _tree won't mutate but Add will replace it
            if (keytree != null)
            {
                hasMatch = parsetable.IsEmpty; // MDAC 86773
                if (!hasMatch)
                {

                    // which key do we follow the key-value chain on
                    for (int i = 0; i < keytree.Length; ++i)
                    {
                        NameValuePermission permitKey = keytree[i];
                        if (permitKey != null)
                        {
                            string keyword = permitKey._value;
#if DEBUG
                            Debug.Assert(permitKey._entry == null, "key member has no restrictions");
#endif
                            if (parsetable.ContainsKey(keyword))
                            {
                                string valueInQuestion = (string)parsetable[keyword];

                                // keyword is restricted to certain values
                                NameValuePermission permitValue = permitKey.CheckKeyForValue(valueInQuestion);
                                if (permitValue != null)
                                {
                                    //value does match - continue the chain down that branch
                                    if (permitValue.CheckValueForKeyPermit(parsetable))
                                    {
                                        hasMatch = true;
                                        // adding a break statement is tempting, but wrong
                                        // user can safely extend their restrictions for current rule to include missing keyword
                                        // i.e. Add("provider=sqloledb;integrated security=sspi", "data provider=", KeyRestrictionBehavior.AllowOnly);
                                        // i.e. Add("data provider=msdatashape;provider=sqloledb;integrated security=sspi", "", KeyRestrictionBehavior.AllowOnly);
                                    }
                                    else
                                    { // failed branch checking
                                        return false;
                                    }
                                }
                                else
                                { // value doesn't match to expected values - fail here
                                    return false;
                                }
                            }
                        }
                        // else try next keyword
                    }
                }
                // partial chain match, either leaf-node by shorter chain or fail mid-chain if ( _restrictions == null)
            }

            DBConnectionString entry = _entry;
            if (entry != null)
            {
                // also checking !hasMatch is tempting, but wrong
                // user can safely extend their restrictions for current rule to include missing keyword
                // i.e. Add("provider=sqloledb;integrated security=sspi", "data provider=", KeyRestrictionBehavior.AllowOnly);
                // i.e. Add("provider=sqloledb;", "integrated security=;", KeyRestrictionBehavior.AllowOnly);
                hasMatch = entry.IsSupersetOf(parsetable);
            }

            return hasMatch; // mid-chain failure
        }

        private NameValuePermission CheckKeyForValue(string keyInQuestion)
        {
            NameValuePermission[] valuetree = _tree; // _tree won't mutate but Add will replace it
            if (valuetree != null)
            {
                for (int i = 0; i < valuetree.Length; ++i)
                {
                    NameValuePermission permitValue = valuetree[i];
                    if (String.Equals(keyInQuestion, permitValue._value, StringComparison.OrdinalIgnoreCase))
                    {
                        return permitValue;
                    }
                }
            }
            return null;
        }

        internal NameValuePermission CopyNameValue()
        {
            return new NameValuePermission(this);
        }
    }
}
