using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Tiamat
{
    public class Octree
    {
        #region Members

        private Octree Parent;
        private Octree[] Children = new Octree[8];
        private BoundingBox Region;
        private byte ActiveNodes;

        List<Entity> Entities;

        public EntityManager EntityManager;
        Queue<Entity> EntitiesPendingInsertion;

        const int NODE_MIN_SIZE = 1;
        const int MAX_ENTITIES_PER_NODE = 1;
        int NodeMaxLifespan = 8;
        int NodeCurrentLifespan = -1;

        static bool TreeIsReady = false;
        static bool TreeIsBuilt = false;
        private bool hasChildren { get { return ActiveNodes != 0; } }
        private bool IsLeaf { get { return (!hasChildren && Entities.Count <= MAX_ENTITIES_PER_NODE); } }
        private bool IsRoot { get { return Parent == null; } }
        private bool RenderingIsEnabled = false;

        public static event EventHandler DidCreateNode;
        public static event EventHandler DidDestroyNode;

        #endregion 

        #region Constructors

        public Octree(BoundingBox region, List<Entity> entities, Octree parent = null)
        {
            Region = new BoundingBox(region.Min * 1.2f, region.Max * 1.2f);
            Entities = entities;
            Parent = parent;
            EntitiesPendingInsertion = new Queue<Entity>();

            if (IsRoot)
                EntityManager = new EntityManager(Entities, (int)Math.Floor(Region.Max.Length()));

            Input.PressedControlKeys += EnableRendering;
            if (DidCreateNode != null) DidCreateNode(null, new EventArgs());
            NodeCurrentLifespan = -1;
        }
        public Octree(BoundingBox region, Octree parent=null)
        {
            Region = region;
            Entities = new List<Entity>();
            EntityManager = new EntityManager(Entities, (int)Math.Floor(Region.Max.LengthSquared() * 0.75f));
            NodeCurrentLifespan = -1;
        }
        public Octree(Octree parent=null)
        {
            Region = new BoundingBox(Vector3.Zero, Vector3.Zero);
            Entities = new List<Entity>();
            EntityManager = new EntityManager(Entities, (int)Math.Floor(Region.Max.LengthSquared() * 0.75f));
            NodeCurrentLifespan = -1;
        }

        private void BuildTree()
        {
            if (IsLeaf)
                return;

            Vector3 dimensions = Region.Max - Region.Min;
            if (dimensions == Vector3.Zero)
            {
                FindEnclosingCube();
                dimensions = Region.Max - Region.Min;
            }

            if (dimensions.X <= NODE_MIN_SIZE && dimensions.Y <= NODE_MIN_SIZE && dimensions.Z <= NODE_MIN_SIZE)
                return;



            Vector3 center = (dimensions / 2f) + Region.Min;

            if (Entities.Count > MAX_ENTITIES_PER_NODE)
            {
                BoundingBox[] octant = new BoundingBox[8];

                octant[0] = new BoundingBox(Region.Min, center);
                octant[1] = new BoundingBox(new Vector3(center.X, Region.Min.Y, Region.Min.Z), new Vector3(Region.Max.X, center.Y, center.Z));
                octant[2] = new BoundingBox(new Vector3(center.X, Region.Min.Y, center.Z), new Vector3(Region.Max.X, center.Y, Region.Max.Z));
                octant[3] = new BoundingBox(new Vector3(Region.Min.X, Region.Min.Y, center.Z), new Vector3(center.X, center.Y, Region.Max.Z));
                octant[4] = new BoundingBox(new Vector3(Region.Min.X, center.Y, Region.Min.Z), new Vector3(center.X, Region.Max.Y, center.Z));
                octant[5] = new BoundingBox(new Vector3(center.X, center.Y, Region.Min.Z), new Vector3(Region.Max.X, Region.Max.Y, center.Z));
                octant[6] = new BoundingBox(center, Region.Max);
                octant[7] = new BoundingBox(new Vector3(Region.Min.X, center.Y, center.Z), new Vector3(center.X, Region.Max.Y, Region.Max.Z));


                List<Entity>[] octEntities = new List<Entity>[8];
                for (int i = 0; i < 8; ++i)
                    octEntities[i] = new List<Entity>();


                List<Entity> delist = new List<Entity>();

                foreach (Entity entity in Entities)
                {
                    for (int a = 0; a < 8; ++a)
                    {
                        if (octant[a].Contains(entity.BoundingBox) == ContainmentType.Contains)
                        {
                            octEntities[a].Add(entity);
                            delist.Add(entity);
                            break;
                        }
                    }
                }

                foreach (Entity entity in delist)
                    Entities.Remove(entity);

                for (int a = 0; a < 8; ++a)
                {
                    if (octEntities[a].Count != 0)
                    {
                        Children[a] = CreateNode(octant[a], octEntities[a]);
                        ActiveNodes |= (byte)(1 << a);
                        Children[a].BuildTree();
                    }
                }
            }

            TreeIsBuilt = true;
            TreeIsReady = true;
        }
        private Octree CreateNode(BoundingBox region, List<Entity> entities)
        {
            if (entities.Count == 0)
                return null;

            Octree ret = new Octree(region, entities, this);
            return ret;
        }
        private Octree CreateNode(BoundingBox region, Entity entity)
        {
            List<Entity> entities = new List<Entity>(1);
            entities.Add(entity);
            Octree ret = new Octree(region, entities, this);
            return ret;
        }

        #endregion

        #region Update Methods

        public void Update(GameTime gameTime)
        {
            if (TreeIsBuilt)
            {
                // Handle Node LifeTimes:
                // If node is empty, countdown lifetime
                // Else double its lifetime up to 64
                if (Entities.Count == 0)
                {
                    if (hasChildren == false)
                    {
                        if (NodeCurrentLifespan == -1)
                            NodeCurrentLifespan = NodeMaxLifespan;
                        else
                            NodeCurrentLifespan -= 1;
                    }
                }
                else
                {
                    if (NodeCurrentLifespan != -1)
                    {
                        if (NodeCurrentLifespan <= 64)
                            NodeCurrentLifespan *= 2;
                        NodeCurrentLifespan -= 1;
                    }
                }


                // Update Node's entities procedure
                List<Entity> movedEntities = EntityManager.EntitiesWillMove(Entities, gameTime);

                // Prune dead objects from the tree
                for (int a = 0; a < Entities.Count; a++)
                {
                    if (!Entities[a].IsAlive)
                    {
                        if (movedEntities.Contains(Entities[a]))
                            movedEntities.Remove(Entities[a]);
                        Entities.RemoveAt(a--);
                    }
                }

                //Update Childnodes Recursively
                for (int flags = ActiveNodes, index = 0; flags > 0; flags >>= 1, index++)
                    if ((flags & 1) == 1) Children[index].Update(gameTime);


                //Check to see if an entity which has moved has passed its containing node's bounds
                foreach (Entity movedentity in movedEntities)
                {
                    Octree current = this;

                    //Recursively find the first parent which contains the entity
                    while (current.Region.Contains(movedentity.BoundingBox) != ContainmentType.Contains)//we must be using a bounding sphere, so check for its containment.
                    {
                        if (current.Parent != null)
                            current = current.Parent;
                        else
                        {
                            break;
                        }
                    }
                    //Remove from the node and insert to first parent that contains it
                    Entities.Remove(movedentity);
                    current.Insert(movedentity);
                }

                //Prune out any dead branches in the tree, removing it from the Active Nodes bitmask
                for (int flags = ActiveNodes, index = 0; flags > 0; flags >>= 1, index++)
                    if ((flags & 1) == 1 && Children[index].NodeCurrentLifespan == 0)
                    {
                        if (DidCreateNode != null) DidDestroyNode(null, new EventArgs());
                        Children[index] = null;
                        ActiveNodes ^= (byte)(1 << index);
                    }


                //Look for collisions
                if (this.IsRoot)
                {
                    //This will recursively gather up all collisions and create a list of them.
                    //this is simply a matter of comparing all objects in the current root node with all objects in all child nodes.
                    //note: we can assume that every collision will only be between objects which have moved.
                    //note 2: An explosion can be centered on a point but grow in size over time. In this case, you'll have to override the update method for the explosion.
                    List<Collision> collisionList = GetCollisionsList(new List<Entity>());

                    foreach (Collision collision in collisionList)
                    {
                        if (collision != null)
                            CollisionManager.HandleSphereToSphereCollision(collision, gameTime);
                    }
                    EntityManager.Update(gameTime);
                }

            }

            else
                BuildTree();
        }
        private void UpdateTree()
        {
            if (!TreeIsBuilt)
            {
                foreach (Entity entity in EntitiesPendingInsertion)
                    Entities.Add(EntitiesPendingInsertion.Dequeue());

                BuildTree();
            }

            else
            {
                foreach (Entity entity in EntitiesPendingInsertion)
                    Insert(EntitiesPendingInsertion.Dequeue());
            }

            TreeIsReady = true;
        }

        #endregion

        #region Collision Methods

        private List<Collision> GetCollisionsList(List<Entity> parentNodeEntities) 
        {
            if (!TreeIsReady)
                UpdateTree();

            string binary = Convert.ToString(ActiveNodes, 2);
            int level = 0;
            Octree parent = this.Parent;
            while (parent != null)
            {
                parent = parent.Parent;
                level++;
            }

            List<Collision> collisions = new List<Collision>();

            // Check parent-node entities against current-node entities
            foreach(Entity prntEntity in parentNodeEntities)
            {
                foreach(Entity chldEntity in Entities)
                {
                    if (chldEntity == prntEntity)
                        continue;

                    Collision collision = CollisionManager.CheckSphereToSphereCollision(prntEntity, chldEntity, this);
                    if (collision != null)
                        collisions.Add(collision);
                }
            }

            // Check current-node entities against current-node entities
            if (Entities.Count > 1)
            {
                for (int i=0 ; i<Entities.Count ; ++i)
                    for (int j=i+1 ; j<Entities.Count ; ++j)
                    {
                        if (Entities[i] == Entities[j])
                            continue;

                        Collision col = CollisionManager.CheckSphereToSphereCollision(Entities[i], Entities[j], this);
                        if (col != null)
                            collisions.Add(col);
                    }
            }

            // Place current-level objects into a list so child nodes can access parent-objects
            foreach (Entity entity in Entities)
                parentNodeEntities.Add(entity);

            //each child node will give us a list of intersection records, which we then merge with our own intersection records.
            for (int flags = ActiveNodes, index = 0; flags > 0; flags >>= 1, index++)
            {
                List<Entity> traversalList = new List<Entity>(parentNodeEntities);

                if ((flags & 1) == 1)
                    collisions.AddRange(Children[index].GetCollisionsList(traversalList));
            }

            return collisions;
        }

        #endregion

        #region Entity Adding / Removal

        /// <summary>
        /// A tree has already been created, so we're going to try to insert an item into the tree without rebuilding the whole thing
        /// </summary>
        /// <typeparam name="T">An Entity object</typeparam>
        /// <param name="Item">The entity to insert into the tree</param>
        private void Insert<T>(T Item) where T : Entity
        {        
            //make sure we're not inserting an object any deeper into the tree than we have to.
            if (this.IsLeaf)
            {
                Entities.Add(Item);
                return;
            }

            //Check to see if the dimensions of the box are greater than the minimum dimensions
            Vector3 dimensions = Region.Max - Region.Min;
            if (dimensions.X <= NODE_MIN_SIZE && dimensions.Y <= NODE_MIN_SIZE && dimensions.Z <= NODE_MIN_SIZE)
            {
                Entities.Add(Item);
                return;
            }
            Vector3 half = dimensions / 2.0f;
            Vector3 center = Region.Min + half;



            //Find or create subdivided regions for each octant in the current region
            BoundingBox[] childOctant = new BoundingBox[8];
            childOctant[0] = (Children[0] != null) ? Children[0].Region : new BoundingBox(Region.Min, center);
            childOctant[1] = (Children[1] != null) ? Children[1].Region : new BoundingBox(new Vector3(center.X, Region.Min.Y, Region.Min.Z), new Vector3(Region.Max.X, center.Y, center.Z));
            childOctant[2] = (Children[2] != null) ? Children[2].Region : new BoundingBox(new Vector3(center.X, Region.Min.Y, center.Z), new Vector3(Region.Max.X, center.Y, Region.Max.Z));
            childOctant[3] = (Children[3] != null) ? Children[3].Region : new BoundingBox(new Vector3(Region.Min.X, Region.Min.Y, center.Z), new Vector3(center.X, center.Y, Region.Max.Z));
            childOctant[4] = (Children[4] != null) ? Children[4].Region : new BoundingBox(new Vector3(Region.Min.X, center.Y, Region.Min.Z), new Vector3(center.X, Region.Max.Y, center.Z));
            childOctant[5] = (Children[5] != null) ? Children[5].Region : new BoundingBox(new Vector3(center.X, center.Y, Region.Min.Z), new Vector3(Region.Max.X, Region.Max.Y, center.Z));
            childOctant[6] = (Children[6] != null) ? Children[6].Region : new BoundingBox(center, Region.Max);
            childOctant[7] = (Children[7] != null) ? Children[7].Region : new BoundingBox(new Vector3(Region.Min.X, center.Y, center.Z), new Vector3(center.X, Region.Max.Y, Region.Max.Z));



            //Is the item completely contained within the root bounding box? Should always be
            //Try to place the entity into a child node
            //If we can't fit it in a child node, then we insert it into the current node
            if (Item.HalfSize != Vector3.Zero && Region.Contains(Item.BoundingBox) == ContainmentType.Contains)
            {
                bool found = false;
                for (int a = 0; a < 8; a++)
                {
                    //is the object contained within a child quadrant?
                    if (childOctant[a].Contains(Item.BoundingBox) == ContainmentType.Contains)
                    {                        
                        found = true;
                        if (Children[a] != null)
                            Children[a].Insert(Item);   //Add the item into that tree and let the child tree figure out what to do with it
                        else
                        {
                            Children[a] = CreateNode(childOctant[a], Item);   //create a new tree node with the item
                            ActiveNodes |= (byte)(1 << a);
                            break;
                        }
                    }
                }
                if (!found) Entities.Add(Item);
            }
            else
            {
                //either the item lies outside of the enclosed bounding box or it is intersecting it. 
                //Either way, we need to rebuild the entire tree by enlarging the containing bounding box
                //BoundingBox enclosingArea = FindBox();
                BuildTree();
            }
        }

        #endregion

        #region Rendering Methods
        public void Draw(PrimitiveBatch primitiveBatch)
        {
            EntityManager.Draw(primitiveBatch);
            if (RenderingIsEnabled)
                Render(primitiveBatch);
        }

        /// <summary>
        /// Renders the current state of the octTree by drawing the outlines of each bounding region.
        /// </summary>
        public void Render(PrimitiveBatch primitiveBatch)
        {
            primitiveBatch.DrawForm(new Cube(Region));
            for (int a = 0; a < 8; a++)
            {
                if (Children[a] != null)
                    Children[a].Render(primitiveBatch);
            }
        }

        public void EnableRendering(object sender, Input.KeyArgs args)
        {
            if (args.PressedKeys.Contains(Input.Actions.EnableOctreeRendering))
                RenderingIsEnabled = !RenderingIsEnabled;
        }

        #endregion

        #region Region Helper Methods

        /// <summary>
        /// This finds the smallest enclosing cube which is a power of 2, for all objects in the list.
        /// </summary>
        private void FindEnclosingCube()
        {
            FindEnclosingBox();

            //find the min offset from (0,0,0) and translate by it for a short while
            Vector3 offset = Region.Min - Vector3.Zero;
            Region.Min += offset;
            Region.Max += offset;

            //find the nearest power of two for the max values
            int highX = (int)Math.Floor(Math.Max(Math.Max(Region.Max.X, Region.Max.Y), Region.Max.Z));

            //see if we're already at a power of 2
            for (int bit = 0; bit < 32; bit++)
            {
                if (highX == 1 << bit)
                {
                    Region.Max = new Vector3(highX, highX, highX);

                    Region.Min -= offset;
                    Region.Max -= offset;
                    return;
                }
            }

            //gets the most significant bit value, so that we essentially do a Ceiling(X) with the 
            //ceiling result being to the nearest power of 2 rather than the nearest integer.
            int x = MathUtils.FindNearestPowerOf2(highX);

            Region.Max = new Vector3(x, x, x);

            Region.Min -= offset;
            Region.Max -= offset;
        }

        /// <summary>
        /// This finds the dimensions of the bounding box necessary to tightly enclose all items in the object list.
        /// </summary>
        private void FindEnclosingBox()
        {
            Vector3 global_min = Region.Min, global_max = Region.Max;



            //go through all the objects in the list and find the extremes for their bounding areas.
            foreach (Entity entity in Entities)
            {
                Vector3 local_min = Vector3.Zero, local_max = Vector3.Zero;

                if (entity.BoundingBox == null)
                {
                    //the object doesn't have any bounding regions associated with it, so we're going to skip it.
                    //otherwise, we'll get stack overflow exceptions since we'd be creating an infinite number of nodes approaching zero.
                    //continue;
                    throw new Exception("Every object in the octTree must have a bounding region!");
                }

                if (entity.BoundingBox != null && entity.HalfSize != Vector3.Zero)
                {

                    local_min = entity.BoundingBox.Min;
                    local_max = entity.BoundingBox.Max;
                }

                if (local_min.X < global_min.X) global_min.X = local_min.X;
                if (local_min.Y < global_min.Y) global_min.Y = local_min.Y;
                if (local_min.Z < global_min.Z) global_min.Z = local_min.Z;

                if (local_max.X > global_max.X) global_max.X = local_max.X;
                if (local_max.Y > global_max.Y) global_max.Y = local_max.Y;
                if (local_max.Z > global_max.Z) global_max.Z = local_max.Z;
            }

            Region.Min = global_min;
            Region.Max = global_max;
        }

        #endregion
    }
}
