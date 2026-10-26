# cm.io.Database

Scope: relational database files and servers, read and write, as one brick (DMP chapter 02, "Foundation bricks are complete").
Today: Microsoft Access files, read only (tables, columns, SELECT), through OleDb; SQL Server instances, one session that runs a batch or reads the first result set (`SqlServerSession`, Microsoft.Data.SqlClient; the caller owns the SQL and the connection string).
Not yet in it: PostgreSQL (still referenced by CM.Sagittarius directly, listed in `DMP/protocol-exceptions.txt`), Access writes, SQL Server schema helpers, pooling policy.
