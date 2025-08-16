using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace NzbDrone.Core.Datastore
{
    public interface IModelWithId
    {
        int Id { get; set; }
    }

    public interface IEmbeddedDocument
    {
    }

    public class ModelBase : IModelWithId
    {
        public int Id { get; set; }
    }

    public interface IRepository<TModel> where TModel : IModelWithId, new()
    {
        TModel Get(int id);
        TModel FindById(int id);
        TModel Insert(TModel model);
        TModel Update(TModel model);
        void Delete(int id);
        void Delete(TModel model);
        List<TModel> All();
        List<TModel> GetMany(IEnumerable<int> ids);
        TModel Single(Expression<Func<TModel, bool>> where);
        TModel SingleOrDefault(Expression<Func<TModel, bool>> where);
        List<TModel> Where(Expression<Func<TModel, bool>> where);
        bool Exists(Expression<Func<TModel, bool>> where);
        void DeleteMany(Expression<Func<TModel, bool>> where);
        void InsertMany(IList<TModel> models);
        void UpdateMany(IList<TModel> models);
        void DeleteMany(IList<TModel> models);
        void Purge(bool vacuum = false);
        bool HasItems();
        void SetFields(TModel model, params Expression<Func<TModel, object>>[] properties);
        TModel Upsert(TModel model);
        void UpsertMany(IList<TModel> models);
    }

    public interface IBasicRepository<TModel> where TModel : ModelBase, new()
    {
        IEnumerable<TModel> All();
        int Count();
        TModel Get(int id);
        IEnumerable<TModel> Get(IEnumerable<int> ids);
        TModel GetBySlug(string slug);
        TModel FindBySlug(string slug);
        TModel Single(Expression<Func<TModel, bool>> where);
        TModel SingleOrDefault(Expression<Func<TModel, bool>> where);
        IEnumerable<TModel> Where(Expression<Func<TModel, bool>> where);
        IEnumerable<TModel> WhereDistinct<TKey>(Expression<Func<TModel, bool>> where, Expression<Func<TModel, TKey>> distinct);
        List<TResult> DistinctBy<TResult>(Expression<Func<TModel, TResult>> property, Expression<Func<TModel, bool>> where);
        PagingSpec<TModel> GetPaged(PagingSpec<TModel> pagingSpec);
        TModel Insert(TModel model);
        void Insert(IList<TModel> models);
        TModel Update(TModel model);
        void Update(IList<TModel> models);
        void UpdateFields(TModel model, params Expression<Func<TModel, object>>[] properties);
        TModel Upsert(TModel model);
        void Delete(int id);
        void Delete(TModel model);
        void Delete(Expression<Func<TModel, bool>> where);
        void Delete(IEnumerable<TModel> models);
        void Purge(bool vacuum = false);
        bool Exists(int id);
        bool Exists(Expression<Func<TModel, bool>> where);
        void SetFields(TModel model, params Expression<Func<TModel, object>>[] properties);
        TModel FindById(int id);
        List<TModel> GetMany(IEnumerable<int> ids);
    }

    public class PagingSpec<TModel>
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalRecords { get; set; }
        public string SortKey { get; set; }
        public SortDirection SortDirection { get; set; }
        public List<TModel> Records { get; set; }

        public PagingSpec()
        {
            Records = new List<TModel>();
        }
    }

    public enum SortDirection
    {
        Ascending,
        Descending
    }
}

