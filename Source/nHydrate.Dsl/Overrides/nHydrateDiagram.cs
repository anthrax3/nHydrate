#pragma warning disable 0168
using System;
using System.Linq;
using nHydrate.Generator.Common.Util;
using Microsoft.VisualStudio.Modeling.Diagrams;
using System.Windows.Forms;
using System.IO;

namespace nHydrate.Dsl
{
    partial class nHydrateDiagram
    {
        ///Determine if this diagram is loading from disk
        public bool IsLoading { get; set; } = false;

        #region Constructors

        //Constructors were not generated for this class because it had HasCustomConstructor
        //set to true. Please provide the constructors below in a partial class.
        public nHydrateDiagram(Microsoft.VisualStudio.Modeling.Store store, params Microsoft.VisualStudio.Modeling.PropertyAssignment[] propertyAssignments)
            : this(store != null ? store.DefaultPartitionForClass(DomainClassId) : null, propertyAssignments)
        {
        }

        public nHydrateDiagram(Microsoft.VisualStudio.Modeling.Partition partition, params Microsoft.VisualStudio.Modeling.PropertyAssignment[] propertyAssignments)
            : base(partition, propertyAssignments)
        {
            this.Store.UndoManager.AddCanUndoRedoCallback(CanUndoRedoCallback);
            this.Store.TransactionManager.AddCanCommitCallback(CanCommitCallback);

            //Custom code so need to override the constructor
            this.ShowGrid = false;
            this.DisplayType = true;
            this.IsLoading = true;
            this.DiagramAdded += new EventHandler<Microsoft.VisualStudio.Modeling.ElementAddedEventArgs>(nHydrateDiagram_DiagramAdded);
            TextManagerEvents.RegisterForTextManagerEvents();
        }

        private bool CanUndoRedoCallback(bool isUndo, Microsoft.VisualStudio.Modeling.TransactionItem transactionItem)
        {
            return true;
        }

        public Microsoft.VisualStudio.Modeling.CanCommitResult CanCommitCallback(Microsoft.VisualStudio.Modeling.Transaction transaction)
        {
            return Microsoft.VisualStudio.Modeling.CanCommitResult.Commit;
        }

        #endregion

        #region Events

        protected override void OnElementAdded(Microsoft.VisualStudio.Modeling.ElementAddedEventArgs e)
        {
            var model = this.ModelElement as nHydrate.Dsl.nHydrateModel;
            if (!model.IsLoading)
            {
                if (e.ModelElement is IPrecedence)
                {
                    var element = e.ModelElement as IPrecedence;
                    using (var transaction = model.Store.TransactionManager.BeginTransaction(Guid.NewGuid().ToString()))
                    {
                        element.PrecedenceOrder = ++model.MaxPrecedenceOrder;
                        transaction.Commit();
                    }
                }
            }

            base.OnElementAdded(e);
        }

        public event EventHandler<ModelElementEventArgs> ShapeDoubleClick;

        protected void OnShapeDoubleClick(ModelElementEventArgs e)
        {
            if (this.ShapeDoubleClick != null)
                ShapeDoubleClick(this, e);
        }

        public event EventHandler<ModelElementEventArgs> ShapeConfiguring;

        protected void OnShapeConfiguring(ModelElementEventArgs e)
        {
            if (this.ShapeConfiguring != null)
                ShapeConfiguring(this, e);
        }

        #endregion

        #region Event Handlers

        private void FieldAdded(object sender, Microsoft.VisualStudio.Modeling.ElementAddedEventArgs e)
        {
            var field = e.ModelElement as Field;
            if (field.Entity == null) return;
            if (field.Entity.nHydrateModel == null) return;

            if (!field.Entity.nHydrateModel.IsLoading && field.SortOrder == 0)
            {
                var maxSortOrder = 1;
                if (field.Entity.Fields.Count > 0)
                    maxSortOrder = field.Entity.Fields.Max(x => x.SortOrder);

                using (var transaction = this.Store.TransactionManager.BeginTransaction(Guid.NewGuid().ToString()))
                {
                    field.SortOrder = ++maxSortOrder;
                    transaction.Commit();
                }
            }
        }

        private void IndexColumnAdded(object sender, Microsoft.VisualStudio.Modeling.ElementAddedEventArgs e)
        {
            var ic = e.ModelElement as IndexColumn;
            if (ic.Index == null) return;
            if (ic.Index.Entity == null) return;
            if (ic.Index.Entity.nHydrateModel == null) return;

            if (!ic.IsInternal && !ic.Index.Entity.nHydrateModel.IsLoading && !this.Store.InUndo)
            {
                if (!ic.Index.Entity.nHydrateModel.IsLoading)
                {
                    if (ic.Index.IndexType != IndexTypeConstants.User)
                        throw new Exception("This is a managed index and cannot be modified.");
                }
            }

            if (!ic.Index.Entity.nHydrateModel.IsLoading)
            {
                if (ic.SortOrder == 0)
                {
                    using (var transaction = this.Store.TransactionManager.BeginTransaction(Guid.NewGuid().ToString()))
                    {
                        var max = 0;
                        if (ic.Index.IndexColumns.Count > 0)
                            max = ic.Index.IndexColumns.Max(x => x.SortOrder);
                        ic.SortOrder = max + 1;
                        transaction.Commit();
                    }
                }
            }

        }

        private void FieldPropertyChanged(object sender, Microsoft.VisualStudio.Modeling.ElementPropertyChangedEventArgs e)
        {
            var f = e.ModelElement as Field;
            f.CachedImage = null;
        }

        private void ViewFieldPropertyChanged(object sender, Microsoft.VisualStudio.Modeling.ElementPropertyChangedEventArgs e)
        {
            var f = e.ModelElement as ViewField;
            f.CachedImage = null;
        }

        private void DiagramViewablePropertyChanged(object sender, Microsoft.VisualStudio.Modeling.ElementPropertyChangedEventArgs e)
        {
            var model = e.ModelElement as nHydrateModel;
            if (model != null)
            {
                if (e.DomainProperty.Name == "DiagramVisibility")
                {
                    var propValue = (VisibilityTypeConstants)e.DomainProperty.GetValue(e.ModelElement);

                    var visibleVW = ((propValue & VisibilityTypeConstants.View) == VisibilityTypeConstants.View);
                    var visibleSP = ((propValue & VisibilityTypeConstants.StoredProcedure) == VisibilityTypeConstants.StoredProcedure);
                    var visibleFN = ((propValue & VisibilityTypeConstants.Function) == VisibilityTypeConstants.Function);

                    {
                        var list = this.Store.ElementDirectory.AllElements
                                       .Where(x => x is StoredProcedureShape)
                                       .ToList()
                                       .Cast<StoredProcedureShape>()
                                       .ToList();

                        if (visibleSP)
                        {
                            list.ForEach(x => x.Show());
                            using (var transaction = model.Store.TransactionManager.BeginTransaction(Guid.NewGuid().ToString()))
                            {
                                list.ForEach(x => x.EnsureCompartments());
                                list.ForEach(x => this.FixUpChildShapes(x));
                                list.ForEach(x => x.RebuildShape());
                                transaction.Commit();
                            }
                        }
                        else list.ForEach(x => x.Hide());
                    }

                    {
                        var list = this.Store.ElementDirectory.AllElements
                                       .Where(x => x is ViewShape)
                                       .ToList()
                                       .Cast<ViewShape>()
                                       .ToList();

                        if (visibleVW)
                        {
                            list.ForEach(x => x.Show());
                            using (var transaction = model.Store.TransactionManager.BeginTransaction(Guid.NewGuid().ToString()))
                            {
                                list.ForEach(x => x.EnsureCompartments());
                                list.ForEach(x => this.FixUpChildShapes(x));
                                list.ForEach(x => x.RebuildShape());
                                transaction.Commit();
                            }
                        }
                        else list.ForEach(x => x.Hide());
                    }

                    {
                        var list = this.Store.ElementDirectory.AllElements
                                       .Where(x => x is FunctionShape)
                                       .ToList()
                                       .Cast<FunctionShape>()
                                       .ToList();

                        if (visibleFN)
                        {
                            list.ForEach(x => x.Show());
                            using (var transaction = model.Store.TransactionManager.BeginTransaction(Guid.NewGuid().ToString()))
                            {
                                list.ForEach(x => x.EnsureCompartments());
                                list.ForEach(x => this.FixUpChildShapes(x));
                                list.ForEach(x => x.RebuildShape());
                                transaction.Commit();
                            }
                        }
                        else list.ForEach(x => x.Hide());
                    }

                }
            }
        }

        private bool _isLoaded = false;

        protected void nHydrateDiagram_DiagramAdded(object sender, Microsoft.VisualStudio.Modeling.ElementAddedEventArgs e)
        {
            if (_isLoaded) return;
            _isLoaded = true;

            var model = this.Diagram.ModelElement as nHydrateModel;
            if ((model.DiagramVisibility & VisibilityTypeConstants.StoredProcedure) == VisibilityTypeConstants.StoredProcedure)
                this.Store.ElementDirectory.AllElements.Where(x => x is StoredProcedureShape).ToList().Cast<StoredProcedureShape>().ToList().ForEach(x => x.Show());
            else
                this.Store.ElementDirectory.AllElements.Where(x => x is StoredProcedureShape).ToList().Cast<StoredProcedureShape>().ToList().ForEach(x => x.Hide());

            if ((model.DiagramVisibility & VisibilityTypeConstants.View) == VisibilityTypeConstants.View)
                this.Store.ElementDirectory.AllElements.Where(x => x is ViewShape).ToList().Cast<ViewShape>().ToList().ForEach(x => x.Show());
            else
                this.Store.ElementDirectory.AllElements.Where(x => x is ViewShape).ToList().Cast<ViewShape>().ToList().ForEach(x => x.Hide());

            if ((model.DiagramVisibility & VisibilityTypeConstants.Function) == VisibilityTypeConstants.Function)
                this.Store.ElementDirectory.AllElements.Where(x => x is FunctionShape).ToList().Cast<FunctionShape>().ToList().ForEach(x => x.Show());
            else
                this.Store.ElementDirectory.AllElements.Where(x => x is FunctionShape).ToList().Cast<FunctionShape>().ToList().ForEach(x => x.Hide());

            //Notify when field is changed so we can refresh icon
            this.Store.EventManagerDirectory.ElementPropertyChanged.Add(
                this.Store.DomainDataDirectory.FindDomainClass(typeof(Field)),
                new EventHandler<Microsoft.VisualStudio.Modeling.ElementPropertyChangedEventArgs>(FieldPropertyChanged));

            this.Store.EventManagerDirectory.ElementPropertyChanged.Add(
                this.Store.DomainDataDirectory.FindDomainClass(typeof(ViewField)),
                new EventHandler<Microsoft.VisualStudio.Modeling.ElementPropertyChangedEventArgs>(ViewFieldPropertyChanged));

            this.Store.EventManagerDirectory.ElementPropertyChanged.Add(
                this.Store.DomainDataDirectory.FindDomainClass(typeof(nHydrateModel)),
                new EventHandler<Microsoft.VisualStudio.Modeling.ElementPropertyChangedEventArgs>(DiagramViewablePropertyChanged));

            //Notify when an index column is added
            this.Store.EventManagerDirectory.ElementAdded.Add(
                this.Store.DomainDataDirectory.FindDomainClass(typeof(IndexColumn)),
                new EventHandler<Microsoft.VisualStudio.Modeling.ElementAddedEventArgs>(IndexColumnAdded));

            //Notify when a Field is added
            this.Store.EventManagerDirectory.ElementAdded.Add(
                this.Store.DomainDataDirectory.FindDomainClass(typeof(Field)),
                new EventHandler<Microsoft.VisualStudio.Modeling.ElementAddedEventArgs>(FieldAdded));

            this.IsLoading = false;
            model.IsDirty = true;

        }

        #endregion

        protected override ShapeElement CreateChildShape(Microsoft.VisualStudio.Modeling.ModelElement element)
        {
            var shape = base.CreateChildShape(element);

            var model = this.ModelElement as nHydrate.Dsl.nHydrateModel;
            if (model != null)
            {
                if (shape is StoredProcedureShape)
                {
                    if ((model.DiagramVisibility & VisibilityTypeConstants.StoredProcedure) == VisibilityTypeConstants.StoredProcedure) shape.Show();
                    else shape.Hide();
                }
                else if (shape is ViewShape)
                {
                    if ((model.DiagramVisibility & VisibilityTypeConstants.View) == VisibilityTypeConstants.View) shape.Show();
                    else shape.Hide();
                }
                else if (shape is FunctionShape)
                {
                    if ((model.DiagramVisibility & VisibilityTypeConstants.Function) == VisibilityTypeConstants.Function) shape.Show();
                    else shape.Hide();
                }

            }

            return shape;
        }

        public override void OnDragOver(DiagramDragEventArgs e)
        {
            if (e.Data.GetFormats().Contains("VSToolboxUniqueID"))
            {
                var model = this.Diagram.ModelElement as nHydrateModel;
                var typeName = (e.Data.GetData("VSToolboxUniqueID") as MemoryStream).GetString().Replace("\0", string.Empty);
                if (typeName == "nHydrate.DslPackage.ViewToolboxItem")
                {
                    if ((model.DiagramVisibility & VisibilityTypeConstants.View) != VisibilityTypeConstants.View)
                    {
                        e.Effect = DragDropEffects.None;
                        return;
                    }
                }
                else if (typeName == "nHydrate.DslPackage.StoredProcedureToolboxItem")
                {
                    if ((model.DiagramVisibility & VisibilityTypeConstants.StoredProcedure) != VisibilityTypeConstants.StoredProcedure)
                    {
                        e.Effect = DragDropEffects.None;
                        return;
                    }
                }
                else if (typeName == "nHydrate.DslPackage.FunctionToolboxItem")
                {
                    if ((model.DiagramVisibility & VisibilityTypeConstants.Function) != VisibilityTypeConstants.Function)
                    {
                        e.Effect = DragDropEffects.None;
                        return;
                    }
                }
            }

            base.OnDragOver(e);
        }

        protected override void OnChildConfiguring(Microsoft.VisualStudio.Modeling.Diagrams.ShapeElement child, bool createdDuringViewFixup)
        {
            base.OnChildConfiguring(child, createdDuringViewFixup);

            try
            {
                if (!this.IsLoading)
                {
                    //Add a default field to entities
                    if (child.ModelElement is Entity)
                    {
                        var item = child.ModelElement as Entity;
                        var model = item.nHydrateModel;
                        if (item.Fields.Count == 0)
                        {
                            var field = new Field(item.Partition)
                                            {
                                                DataType = DataTypeConstants.Int,
                                                Identity = IdentityTypeConstants.Database,
                                                Name = "ID",
                                            };
                            item.Fields.Add(field); //Add then set PK so it will trigger index code
                            field.IsPrimaryKey = true;
                        }

                        #region Pasting
                        //If there are invalid indexes then try to remap them
                        foreach (var index in item.Indexes.Where(x => x.FieldList.Any(z => z == null)))
                        {
                            foreach (var c in index.IndexColumns.Where(x => x.Field == null && x.FieldID != Guid.Empty))
                            {
                                var f = model.Entities.SelectMany(x => x.Fields).FirstOrDefault(x => x.Id == c.FieldID);
                                if (f != null)
                                {
                                    var f2 = item.Fields.FirstOrDefault(x => x.Name == f.Name);
                                    if (f2 != null)
                                        c.FieldID = f2.Id;
                                }
                            }
                        }

                        //Add a PK index if not one
                        if (!item.Indexes.Any(x => x.IndexType == IndexTypeConstants.PrimaryKey) && item.PrimaryKeyFields.Count > 0)
                        {
                            var index = new Index(item.Partition) { IndexType = IndexTypeConstants.PrimaryKey };
                            item.Indexes.Add(index);
                            var loop = 0;
                            foreach (var field in item.PrimaryKeyFields)
                            {
                                var newIndexColumn = new IndexColumn(item.Partition);
                                index.IndexColumns.Add(newIndexColumn);
                                newIndexColumn.FieldID = field.Id;
                                newIndexColumn.SortOrder = loop;
                                loop++;
                            }
                        }
                        #endregion

                    }
                    else if (child.ModelElement is View)
                    {
                        var item = child.ModelElement as View;
                        if (item.Fields.Count == 0)
                        {
                            var field = new ViewField(item.Partition)
                                            {
                                                DataType = DataTypeConstants.Int,
                                                Name = "Field1",
                                            };
                            item.Fields.Add(field);
                        }
                    }
                    else if (child.ModelElement is StoredProcedure)
                    {
                        var item = child.ModelElement as StoredProcedure;
                        if (item.Fields.Count == 0)
                        {
                            var field = new StoredProcedureField(item.Partition)
                                            {
                                                DataType = DataTypeConstants.Int,
                                                Name = "Field1",
                                            };
                            item.Fields.Add(field);
                        }
                        if (item.Parameters.Count == 0)
                        {
                            var field = new StoredProcedureParameter(item.Partition)
                                            {
                                                DataType = DataTypeConstants.Int,
                                                Name = "Parameter1",
                                            };
                            item.Parameters.Add(field);
                        }
                    }
                    else if (child.ModelElement is Function)
                    {
                        var item = child.ModelElement as Function;
                        if (item.Fields.Count == 0)
                        {
                            var field = new FunctionField(item.Partition)
                                            {
                                                DataType = DataTypeConstants.Int,
                                                Name = "Field1",
                                            };
                            item.Fields.Add(field);
                        }
                        if (item.Parameters.Count == 0)
                        {
                            var field = new FunctionParameter(item.Partition)
                                            {
                                                DataType = DataTypeConstants.Int,
                                                Name = "Parameter1",
                                            };
                            item.Parameters.Add(field);
                        }
                    }

                    this.OnShapeConfiguring(new ModelElementEventArgs() { Shape = child });
                }

            }
            catch (Exception ex)
            {
                throw;
            }
        }

        internal void NotifyShapeDoubleClick(Microsoft.VisualStudio.Modeling.Diagrams.ShapeElement shape)
        {
            this.OnShapeDoubleClick(new ModelElementEventArgs() { Shape = shape });
        }

        protected override void InitializeResources(Microsoft.VisualStudio.Modeling.Diagrams.StyleSet classStyleSet)
        {
            var dte = GetService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            nHydrate.Generator.Common.Util.EnvDTEHelper.Instance.SetDTE(dte);
        }

    }

}