﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.ListViewItem;

namespace TraXile
{
    class ListViewManager
    {
        private ListView _listView;
        private bool _isFiltered;
        private List<ListViewItem> _masterList, _filteredList;
        private Dictionary<string, ListViewItem> _itemMap;

        public ListViewManager(ListView lv_to_manage)
        {
            _listView = lv_to_manage;
            _masterList = new List<ListViewItem>();
            _filteredList = new List<ListViewItem>();
            _itemMap = new Dictionary<string, ListViewItem>();

            foreach(ListViewItem lvi in _listView.Items)
            {
                _masterList.Add(lvi);
                _filteredList.Add(lvi);
                _itemMap.Add(lvi.Name, lvi);
            }
        }

        public void ApplyFullTextFilter(string s_filter)
        {
            List<string> names = new List<string>();

            foreach(ListViewItem lvi in _masterList)
            {
                if(lvi.Text.ToLower().Contains(s_filter.ToLower()))
                {
                    if(!names.Contains(lvi.Name))
                    {
                        names.Add(lvi.Name);
                    }
                }
                else
                {
                    foreach(ListViewSubItem si in lvi.SubItems)
                    {
                        if(si.Text.ToLower().Contains(s_filter.ToLower()))
                        {
                            if (!names.Contains(lvi.Name))
                            {
                                names.Add(lvi.Name);
                            }
                            continue;
                        }
                    }
                }
            }
            FilterByNameList(names);
        }

        public void FilterByNameList(List<string> item_names)
        {
            _filteredList.Clear();
            foreach(string s in item_names)
            {
                if(_itemMap.ContainsKey(s))
                {
                    _filteredList.Add(_itemMap[s]);
                }
            }

            _listView.Items.Clear();

            foreach(ListViewItem lvi in _filteredList)
            {
                _listView.Items.Add(lvi);
            }
            _isFiltered = true;
        }

        public void Reset()
        {
            _listView.Items.Clear();

            foreach (ListViewItem lvi in _masterList)
            {
                _listView.Items.Add(lvi);
            }
            _isFiltered = false;
        }

        public void ClearLvItems()
        {
            _masterList.Clear();
            _itemMap.Clear();
            _listView.Items.Clear();
        }

        public void AddLvItem(ListViewItem lvi, string s_name)
        {
            if(!_itemMap.ContainsKey(s_name))
            {
                lvi.Name = s_name;
                _masterList.Add(lvi);
                _itemMap.Add(s_name, lvi);
                if (!_isFiltered)
                {
                    _listView.Items.Add(lvi);
                }
            }
        }

        public void InsertLvItem(ListViewItem lvi, string s_name, int i_pos)
        {
            if(!_itemMap.ContainsKey(s_name))
            {
                lvi.Name = s_name;
                _masterList.Insert(i_pos, lvi);
                _itemMap.Add(s_name, lvi);
                if (!_isFiltered)
                {
                    _listView.Items.Insert(i_pos, lvi);
                }
            }
        }

        public ListView listView
        {
            get { return _listView; }
        }

        public ListViewItem GetLvItem(string s_name)
        {
            return _itemMap[s_name];
        }

        public ListView.ColumnHeaderCollection Columns
        {
            get { return _listView.Columns; }
        }
    }
}