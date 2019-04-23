# &spades; ACE - ADO Cache Engine 

ADO Cache Engine is an ORM (Object-relational mapping) tool for MS SQL Server build around the idea of caching all the required data in RAM memory of the host to decrease data access times.

ACE can be used as your standard ORM, but it main strength lies in storing data on-site where you can make us of indexing tools it provides. It's designed to be used as a provider for single point data layer solutions for web applications and service-based solutions.

## Features
* No dependency on external libraries.
* Unlimited number of tables.
* Easy-to-use caching API.
* Caching subset of data using lambda expression syntax.
* Caching data related to data already cached using lambda expression syntax relation definition.
* CRUD operations support.
* Live write operation on DB - Engine reflects any changes done on its items to the database, ensuring they're up to date.
* Indexing functionality for faster querying.
* Can work in non-cache mode if you really try.
* Automatic generation of model classes using T4 scripts through [ACE ADO Cache Engine model generation](https://github.com/saklis/ace-model-generation).

## Compatibilty
* C# 7.0 and greater
* MS SQL Server 2008 R2 and greater (older version was not tested, but should be compatibile)

## Getting started with Cache Engine
The Engine object is responsible for holding Cache items. One Engine object can handle up to one database and contains methods to create and remove new cached items (tables).

### Creating new Engine instance
To create an Engine instance just use a constructor, passing connection string as parameter.
```c#
var conStr = "DataSource=db_server;InitialCatalog=db_name;UserID=user;Password=Pa$$w00rd";
var engine = new AdoCacheEngine(conStr);
```

### Data model
Engine instance can be used to create and destroy ADO Cache Items. Item is responsible for all interaction of Engine with particular table.

To create new ADO Cache Item you first need a model class. This class needs to inherits from `AdoCacheEntity` and provides few useful parameters that help the Engine in properly handling tables. For more detail about technicalieties of proper model class creation (as well as T4 scripts to generate classes for you) please refer to [ACE ADO Cache Engine model generation](https://github.com/saklis/ace-model-generation).

As an example, here's some simple model class generated by T4 script:
```c#
[TableName("[User]")]
public partial class User  : AdoCacheEntity {
    public User() : base(false) { }
    protected User(bool isManagedByCacheEngine) : base(isManagedByCacheEngine) { }

    [CurrentValueField("Id")]
    protected System.Int32 _Id_current;
    [NewValueField("Id")]
    protected System.Int32 _Id_new;
    [Key]
    [AutoIncrement]
    [ReadOnly]
    public System.Int32 Id { 
        get => _Id_current;
        set {
            if (IsManagedByCacheEngine) _Id_new = value;
            else _Id_current = value;
        }
    }

    [CurrentValueField("Name")]
    protected System.String _Name_current;
    [NewValueField("Name")]
    protected System.String _Name_new;
    public System.String Name { 
        get => _Name_current;
        set {
            if (IsManagedByCacheEngine) _Name_new = value;
            else _Name_current = value;
        }
    }

    protected override void CopyNewValues(){
        _Id_current = _Id_new;
	_Name_current = _Name_new;
    }

    public override void UndoPendingChanges(){
        _Id_new = _Id_current;
	_Name_new = _Name_current;
    }
}
```

### Cache Item creation
To create new ADO Cache Item associated with created Engine instance you can use CreateItem method. 
```c#
engine.CreateItem<User>();
```

You can create as many Cache Items as you need (or your host's memory allows) as long as all are based on different classes.

At this point you can also configure newly created Item by using `AdoCacheItemOptions` object.
```c#
engine.CreateCache<Order>(new AdoCacheItemOptions {
    OverrideTableName = "DifferentTableName", // Replace default table name declared in the model with this
    EnableReadOnlyColumnsSupport = true // Enable support for columns/properties marked with [ReadOnly] attribute
});
```

### Accessing created Item.
To access any of the created items use Item() method.
```c#
var item = engine.Item<User>();
```

You can also create reference to it directly from CreateItem().
```c#
var item = engine.CreateItem<User>();
```

### Loading data to cache.
By default, cache doesn't contain any data. There are couple of ways of loading data and they all serve a bit different purpose.

The most common example will be just loading all data for caching purposes.
```c#
engine.CreateItem<User>().LoadAll();
```

You may also use simple lambda expressions to load only subset of data, like for example only users that name starts with 'S'.
```c#
engine.CreateItem<User>.LoadWhere(u => u.Name.StartsWith("S"));
```

Last but not least, in case you need to load only data that is in relation with another Item, you can use LoadRelatedWith() method.
```c#
engine.CreateItem<Group>.LoadWhere(g => g.Name == "Admins");
var groupItem = engine.Item<Group>();

engine.CreateItem<User>().LoadRelatedWith(groupItem, (user, group) => {user.Id == group.MemberId});
```

## CRUD operations
Using Cache Item member methods you can edit data existing already in cache, add new and remove old objects/records. Any changes you make in Cached Item will be mirrored into connected database. This includes even the instance that have no data loaded.

### INSERT - add new objects to cache
To add new object to cache you should just create new instance of model class, set all required properties and then call Insert() method that is part of proper Item object.
```c#
var newUser = new User { Name = "Brian" };
var insertedUser = engine.Item<User>().Insert(newUser);
```

In this example insertedUser holds a reference to an object that is part of cache, while newUser can be safelly discraded. Also, as in this example property Id is marked with [AutoIncrement] attribute, Cache Engine will assign value to the property, which you can check in insertedUser object.

### SELECT - read existing objects
To gain access to collection of already existing entities in the cache you can refer to Entities List that's part of Cache Item.
```c#
var objects = engine.Item<User>().Entities
```

### UPDATE - change existing data
Before you can edit any data you need to get an object that is actually part of the Cached Item. After that editing and updating data happen similiary to inserting new object.
```c#
var userToUpdate = engine.Item<User>().Entities.Single(u => u.Id = 42);
userToUpdate.Name = "Adams";
engine.Item<User>().Update(userToUpdate);
```

### DELETE - removing existing data
Removing data is similiar to Updating. It's advised to not mix those two, though...
```c#
var userToDelete = engine.Item<User>().Entities.Single(u => u.Id = 42);
engine.Item<User>().Delete(userToDelete);
```

## Indexes and Dictionaries
Indexes and Dictionaries are used in ACE for pre-aggregation of data. They allow you define Properties that will be used as aggregation keys and then such construction can be accessed for very fast data search. In a way, they work similiar to Indexes in SQL, but are implemented using Dictionary<TKey, TValue> structures. There's very little difference between Indexes and Dictionaries in ACE, with Dictionary being basically a special case of Index that's optimized to be used for unique Properties, such as [Key] attributes.

### Creating and using an Index
To create and Index you can use BuildIndex() method that's a member of ADO Cache Item. As a parameter this method accepts name of property you want to create aggregation on.
```c#
engine.Item<User>().BuildIndex(nameof(User.Name));
```
Mind that this operation, same as loading methods, can be time consuming.

With Index created, it'll be automatically kept up do date with any changes done to the cached items.
To find cached items with Index use FindInIndex() method, which takes name of Property and value as parameters.
```c#
var items = engine.Item<User>().FindInIndex(nameof(User.Name), "Adams");
```

FindInIndex() method returns List of objects in which the Indexed Property have provided value.

### Creating and using Dictionaries
Dictionaries are special case of Indexes, that can be used in situations when aggregation is performed on unique values, such as keys. Dictionaries are created using `BuildDictionary` method and searched using method `FindInDictionary`.

## Concurrency
In case of concurent operations, you can use `ConcurrentAdoCacheEngine` and `ConcurrentAdoCacheItem` for simple lock-based solution.

## Third party code
* For parsing expression trees into SQL WHERE clauses I used the code from Ryan Ohs published on his blog.
You can find the post here: http://ryanohs.com/2016/04/generating-sql-from-expression-trees-part-2/
