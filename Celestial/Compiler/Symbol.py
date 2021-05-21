class Symbol:

    def __init__(self, _name="", _type="", _params=[], _scope="", _isParam=False, _isMethod=False, _isFunction=False, _isInvariant=False, _isMap=False, _mapKeyType="", _isEvent=False, _returnVar="", _isEnumValue=False, _isStructType=False, _isStructObject=False, _fieldOfStruct="", _isStructField=False, _isQuantifierVar=False, _isLocal=False, _isContractObject=False):
        self.name = _name
        self.type = _type
        self.params = _params
        self.scope = _scope
        self.isParam = _isParam
        self.isMethod = _isMethod
        self.isFunction = _isFunction
        self.isInvariant = _isInvariant
        self.isMap = _isMap
        self.mapKeyType = _mapKeyType
        self.isEvent = _isEvent
        self.returnVar = _returnVar
        self.isEnumValue = _isEnumValue
        self.isStructType = _isStructType
        self.isStructObject = _isStructObject
        self.fieldOfStruct = _fieldOfStruct
        self.isStructField = _isStructField
        self.isQuantifierVar = _isQuantifierVar
        self.isLocal = _isLocal
        self.isContractObject = _isContractObject

    def __eq__(self, otherObj):
        return (self.name == otherObj.name and self.type == otherObj.type and self.params == otherObj.params and self.scope == otherObj.scope and self.isParam == otherObj.isParam and self.isMethod == otherObj.isMethod and self.isFunction == otherObj.isFunction and self.isInvariant == otherObj.isInvariant and self.isMap == otherObj.isMap and self.mapKeyType == otherObj.mapKeyType and self.isEvent == otherObj.isEvent and self.returnVar == otherObj.returnVar and self.isEnumValue == otherObj.isEnumValue and self.isStructType == otherObj.isStructType and self.isStructObject == otherObj.isStructObject and self.fieldOfStruct == otherObj.fieldOfStruct and self.isStructField == otherObj.isStructField and self.isQuantifierVar == otherObj.isQuantifierVar)