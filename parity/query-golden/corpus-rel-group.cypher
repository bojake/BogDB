-- SCHEMA
NODE A id:INT64
NODE B id:INT64

-- SETUP
CREATE (:A {id: 1});
CREATE (:B {id: 2});

-- QUERY: create_rel_group
CREATE REL TABLE GROUP MyGroup ( FROM A TO B );
