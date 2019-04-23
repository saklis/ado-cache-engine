# &spades; ACE - ADO Cache Engine 

ADO Cache Engine is an ORM (Object-relational mapping) tool for MS SQL Server build around the idea of caching all the required data in RAM memory of the host to decrease data access times.

ACE can be used as your standard ORM, but it main strength lies in storing data on-site where you can make us of indexing tools it provides. It's designed to be used as a provider for single point data layer solutions for web applications and service-based solutions.

## Features
* Unlimited number of tables.
* Easy-to-use caching API.
* Caching subset of data using lambda expression syntax.
* Caching data related to data already cached using lambda expression syntax relation definition.
* CRUD operations support.
* Can work in non-cache mode if you really try.

## Compatibilty
* C# 7.0 and greater
* MS SQL Server 2008 R2 and greater (older version was not tested, but should be compatibile)

## Engine
The Engine object is responsible for holding Cache items. One Engine object can handle up to one database and contains methods to create and remove new cached items (tables).

### Creating new Engine instance
To create an Engine instance just use a constructor, passing connection string as parameter.
```c#
var conStr = "DataSource=db_server;InitialCatalog=db_name;UserID=user;Password=Pa$$w00rd";
var engine = new AdoCacheEngine(conStr);
```

### Creating new Items
Engine instance can be used to create and destroy ADO Cache Items. Item is responsible for all interaction of Engine with particular table.

## Third party code
* For parsing expression trees into SQL WHERE clauses I used the code from Ryan Ohs published on his blog.
You can find the post here: http://ryanohs.com/2016/04/generating-sql-from-expression-trees-part-2/
