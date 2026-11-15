"""A business schema context as .NET sends it (camelCase), reduced from the seeded catalog."""

OPERATORS = [
    {
        "key": "eq",
        "label": "שווה ל",
        "allowedTypes": ["text", "integer", "decimal", "date", "boolean", "enum"],
        "shape": "value",
        "allowsFieldOperand": True,
    },
    {
        "key": "lt",
        "label": "קטן מ",
        "allowedTypes": ["integer", "decimal", "date"],
        "shape": "value",
        "allowsFieldOperand": True,
    },
    {
        "key": "gt",
        "label": "גדול מ",
        "allowedTypes": ["integer", "decimal", "date"],
        "shape": "value",
        "allowsFieldOperand": True,
    },
    {
        "key": "between",
        "label": "בין",
        "allowedTypes": ["integer", "decimal", "date"],
        "shape": "range",
        "allowsFieldOperand": False,
    },
    {
        "key": "isEmpty",
        "label": "ריק",
        "allowedTypes": ["text", "integer", "decimal", "date", "boolean", "enum"],
        "shape": "none",
        "allowsFieldOperand": False,
    },
]

CONTEXT = {
    "entities": [
        {
            "key": "employee",
            "displayName": "עובד",
            "displayNamePlural": "עובדים",
            "description": "עובדי הארגון",
            "fields": [
                {"key": "isActive", "displayName": "עובד פעיל", "dataType": "boolean"},
                {
                    "key": "age",
                    "displayName": "גיל",
                    "description": "גיל העובד בשנים מלאות",
                    "dataType": "integer",
                },
                {"key": "salary", "displayName": "שכר חודשי", "dataType": "decimal", "unit": "₪"},
                {"key": "hireDate", "displayName": "תאריך תחילת העסקה", "dataType": "date"},
                {"key": "terminationDate", "displayName": "תאריך סיום העסקה", "dataType": "date"},
            ],
            "relations": [
                {
                    "key": "department",
                    "displayName": "מחלקה",
                    "targetEntity": "department",
                    "cardinality": "manyToOne",
                },
            ],
        },
        {
            "key": "department",
            "displayName": "מחלקה",
            "displayNamePlural": "מחלקות",
            "fields": [{"key": "isActive", "displayName": "מחלקה פעילה", "dataType": "boolean"}],
            "relations": [
                {
                    "key": "employees",
                    "displayName": "עובדים",
                    "targetEntity": "employee",
                    "cardinality": "oneToMany",
                },
            ],
        },
    ],
    "operators": OPERATORS,
    "maxRelationHops": 2,
}
