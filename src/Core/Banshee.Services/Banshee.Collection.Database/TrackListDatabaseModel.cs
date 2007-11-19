//
// TrackListDatabaseModel.cs
//
// Author:
//   Aaron Bockover <abockover@novell.com>
//   Gabriel Burt <gburt@novell.com>
//
// Copyright (C) 2007 Novell, Inc.
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Data;
using System.Text;
using System.Collections.Generic;

using Hyena.Data;
using Hyena.Data.Query;

using Banshee.Database;

namespace Banshee.Collection.Database
{
    public class TrackListDatabaseModel : TrackListModel, IExportableModel, ICacheableModel, 
        IDatabaseModel<TrackInfo>, IFilterable, ISortable, ICareAboutView
    {
        private BansheeDbConnection connection;
        private BansheeCacheableModelAdapter<TrackInfo> cache;
        private int count;
        private int unfiltered_count;
        
        private ISortableColumn sort_column;
        private string sort_query;
        
        private Dictionary<string, string> filter_field_map = new Dictionary<string, string>();
        private string reload_fragment;
        private string join_fragment, condition;

        private string filter_query;
        private string filter;
        private string artist_id_filter_query;
        private string album_id_filter_query;
        
        private int rows_in_view;

        public TrackListDatabaseModel (BansheeDbConnection connection)
        {
            cache = new BansheeCacheableModelAdapter<TrackInfo> (connection, this);

            filter_field_map.Add("track", "CoreTracks.TrackNumber");
            filter_field_map.Add("artist", "CoreArtists.Name");
            filter_field_map.Add("album", "CoreAlbums.Title");
            filter_field_map.Add("title", "CoreTracks.Title");
            
            this.connection = connection;

            Refilter ();
        }
        
        private void GenerateFilterQueryPart()
        {
            if (String.IsNullOrEmpty(Filter)) {
                filter_query = null;
            } else {
                filter_query = String.Format(@"
                    AND (CoreTracks.Title LIKE '%{0}%' 
                        OR CoreArtists.Name LIKE '%{0}%'
                        OR CoreAlbums.Title LIKE '%{0}%')", Filter);
            }
        }
        
        private string AscDesc ()
        {
            return sort_column.SortType == SortType.Ascending ? " ASC" : " DESC";
        }

        private void GenerateSortQueryPart()
        {
            if(sort_column == null) {
                sort_query = null;
                return;
            }
            
            switch(sort_column.SortKey) {
                case "track":
                    sort_query = String.Format (@"
                        lower(CoreArtists.Name) ASC, 
                        lower(CoreAlbums.Title) ASC, 
                        CoreTracks.TrackNumber {0}", AscDesc ()); 
                    break;
                case "artist":
                    sort_query = String.Format (@"
                        lower(CoreArtists.Name) {0}, 
                        lower(CoreAlbums.Title) ASC, 
                        CoreTracks.TrackNumber ASC", AscDesc ()); 
                    break;
                case "album":
                    sort_query = String.Format (@"
                        lower(CoreAlbums.Title) {0},
                        lower(CoreArtists.Name) ASC,
                        CoreTracks.TrackNumber ASC", AscDesc ());
                    break;
                case "title":
                    sort_query = String.Format (@"
                        lower(CoreArtists.Name) ASC, 
                        lower(CoreAlbums.Title) ASC, 
                        lower(CoreTracks.Title) {0}", AscDesc ()); 
                    break;
                default:
                    sort_query = null;
                    return;
            }
        }

        public void Refilter()
        {
            lock(this) {
                GenerateFilterQueryPart();
                cache.Clear ();
            }
        }
        
        public void Sort(ISortableColumn column)
        {
            lock(this) {
                if(sort_column == column && sort_column != null) {
                    sort_column.SortType = sort_column.SortType == SortType.Ascending 
                        ? SortType.Descending 
                        : SortType.Ascending;
                }
            
                sort_column = column;
            
                GenerateSortQueryPart();
                cache.Clear ();
            }
        }
        
        public override void Clear()
        {
            cache.Clear ();
            unfiltered_count = 0;
            count = 0;
            OnCleared();
        }
        
        public override void Reload()
        {
            StringBuilder qb = new StringBuilder ();
            string unfiltered_query = String.Format ("FROM CoreTracks, CoreAlbums, CoreArtists{0} WHERE {1} {2}",
                JoinFragment, FetchCondition, ConditionFragment);
                
            qb.Append (unfiltered_query);
            
            if (artist_id_filter_query != null) {
                qb.Append ("AND ");
                qb.Append (artist_id_filter_query);
            }
                    
            if (album_id_filter_query != null) {
                qb.Append ("AND ");
                qb.Append (album_id_filter_query);
            }
            
            if (filter_query != null)
                qb.Append (filter_query);
            
            if (sort_query != null) {
                qb.Append (" ORDER BY ");
                qb.Append (sort_query);
            }
                
            reload_fragment = qb.ToString ();
            count = cache.Reload ();

            IDbCommand command = connection.CreateCommand ();
            command.CommandText = String.Format ("SELECT COUNT(*) {0}", unfiltered_query);
            unfiltered_count = Convert.ToInt32 (command.ExecuteScalar ());

            OnReloaded ();
        }
                
        public override int IndexOf (TrackInfo track)
        {
            if (track is LibraryTrackInfo) {
                return ((LibraryTrackInfo)track).DbIndex;
            }
            
            return -1;
        }

        public override TrackInfo this[int index] {
            get { return cache.GetValue (index); }
        }
        
        public override int Count {
            get { return count; }
        }
        
        public int UnfilteredCount {
            get { return unfiltered_count; }
        }
        
        public string Filter {
            get { return filter; }
            set { 
                lock(this) {
                    filter = value; 
                }
            }
        }

        public string JoinFragment {
            get { return join_fragment; }
            set {
                join_fragment = value;
                Refilter();
            }
        }

        public string Condition {
            get { return condition; }
            set {
                condition = value;
                Refilter();
            }
        }

        public string ConditionFragment {
            get { return PrefixCondition ("AND"); }
        }

        private string PrefixCondition (string prefix)
        {
            string condition = Condition;
            if (condition == null || condition == String.Empty)
                return String.Empty;
            else
                return String.Format (" {0} {1} ", prefix, condition);
        }
        
        public override IEnumerable<ArtistInfo> ArtistInfoFilter {
            set { 
                ModelHelper.BuildIdFilter<ArtistInfo>(value, "CoreTracks.ArtistID", artist_id_filter_query,
                    delegate(ArtistInfo artist) {
                        if(!(artist is LibraryArtistInfo)) {
                            return null;
                        }
                        
                        return ((LibraryArtistInfo)artist).DbId.ToString();
                    },
                
                    delegate(string new_filter) {
                        artist_id_filter_query = new_filter;
                        Refilter();
                        Reload();
                    }
                );
            }
        }
        
        public override IEnumerable<AlbumInfo> AlbumInfoFilter {
            set { 
                ModelHelper.BuildIdFilter<AlbumInfo>(value, "CoreTracks.AlbumID", album_id_filter_query,
                    delegate(AlbumInfo album) {
                        if(!(album is LibraryAlbumInfo)) {
                            return null;
                        }
                        
                        return ((LibraryAlbumInfo)album).DbId.ToString();
                    },
                
                    delegate(string new_filter) {
                        album_id_filter_query = new_filter;
                        Refilter();
                        Reload();
                    }
                );
            }
        }

        public int CacheId {
            get { return cache.CacheId; }
        }

        public ISortableColumn SortColumn { 
            get { return sort_column; }
        }
                
        public virtual int RowsInView {
            protected get { return rows_in_view; }
            set { rows_in_view = value; }
        }

        int IExportableModel.GetLength() 
        {
            return Count;
        }
        
        IDictionary<string, object> IExportableModel.GetMetadata(int index)
        {
            return this[index].GenerateExportable();
        }

        // Implement ICacheableModel
        public int FetchCount {
            get { return RowsInView > 0 ? RowsInView * 5 : 100; }
        }

        // Implement IDatabaseModel
        public TrackInfo GetItemFromReader (IDataReader reader, int index)
        {
            return new LibraryTrackInfo (reader, index);
        }

        private const string primary_key = "CoreTracks.TrackID";
        public string PrimaryKey {
            get { return primary_key; }
        }

        public string ReloadFragment {
            get { return reload_fragment; }
        }

        public string FetchColumns {
            get { return "CoreTracks.*, CoreArtists.Name, CoreAlbums.Title"; }
        }

        public string FetchFrom {
            get { return "CoreTracks, CoreArtists, CoreAlbums"; }
        }

        public string FetchCondition {
            get { return "CoreArtists.ArtistID = CoreTracks.ArtistID AND CoreAlbums.AlbumID = CoreTracks.AlbumID"; }
        }
    }
}
