"""
Integration tests (auto-generated -- do not edit manually).
Edit integration_client.py and re-run generate_test_integration.py instead.

Prerequisites:
  - Unity Editor must be running
  - Click "UniLiquidLink > Start Integration Test Server" in the Unity menu (port 8700)

All tests are skipped if Unity is not available.
"""
import pytest
from conftest import assert_golden
from integration_client import IntegrationClient, client  # noqa: F401

@pytest.mark.asyncio(loop_scope="session")
async def test_add_abbreviated_namespaces(client):
    assert_golden("add_abbreviated_namespaces", await client._exec(IntegrationClient.run_add_abbreviated_namespaces))

@pytest.mark.asyncio(loop_scope="session")
async def test_call_unknown_method(client):
    assert_golden("call_unknown_method", await client._exec(IntegrationClient.run_call_unknown_method))

@pytest.mark.asyncio(loop_scope="session")
async def test_chained_game_object(client):
    assert_golden("chained_game_object", await client._exec(IntegrationClient.run_chained_game_object))

@pytest.mark.asyncio(loop_scope="session")
async def test_find_test_game_object(client):
    assert_golden("find_test_game_object", await client._exec(IntegrationClient.run_find_test_game_object))

@pytest.mark.asyncio(loop_scope="session")
async def test_find_test_object(client):
    assert_golden("find_test_object", await client._exec(IntegrationClient.run_find_test_object))

@pytest.mark.asyncio(loop_scope="session")
async def test_get_transform(client):
    assert_golden("get_transform", await client._exec(IntegrationClient.run_get_transform))

@pytest.mark.asyncio(loop_scope="session")
async def test_load_asset_database(client):
    assert_golden("load_asset_database", await client._exec(IntegrationClient.run_load_asset_database))

@pytest.mark.asyncio(loop_scope="session")
async def test_load_asset_with_type(client):
    assert_golden("load_asset_with_type", await client._exec(IntegrationClient.run_load_asset_with_type))

@pytest.mark.asyncio(loop_scope="session")
async def test_nested_static_class(client):
    assert_golden("nested_static_class", await client._exec(IntegrationClient.run_nested_static_class))

@pytest.mark.asyncio(loop_scope="session")
async def test_object_method(client):
    assert_golden("object_method", await client._exec(IntegrationClient.run_object_method))

@pytest.mark.asyncio(loop_scope="session")
async def test_object_overload(client):
    assert_golden("object_overload", await client._exec(IntegrationClient.run_object_overload))

@pytest.mark.asyncio(loop_scope="session")
async def test_object_properties(client):
    assert_golden("object_properties", await client._exec(IntegrationClient.run_object_properties))

@pytest.mark.asyncio(loop_scope="session")
async def test_rotate(client):
    assert_golden("rotate", await client._exec(IntegrationClient.run_rotate))

@pytest.mark.asyncio(loop_scope="session")
async def test_sample_enum(client):
    assert_golden("sample_enum", await client._exec(IntegrationClient.run_sample_enum))

@pytest.mark.asyncio(loop_scope="session")
async def test_sample_game_object(client):
    assert_golden("sample_game_object", await client._exec(IntegrationClient.run_sample_game_object))

@pytest.mark.asyncio(loop_scope="session")
async def test_sample_game_object_array(client):
    assert_golden("sample_game_object_array", await client._exec(IntegrationClient.run_sample_game_object_array))

@pytest.mark.asyncio(loop_scope="session")
async def test_sample_game_object_dict(client):
    assert_golden("sample_game_object_dict", await client._exec(IntegrationClient.run_sample_game_object_dict))

@pytest.mark.asyncio(loop_scope="session")
async def test_sample_game_object_list(client):
    assert_golden("sample_game_object_list", await client._exec(IntegrationClient.run_sample_game_object_list))

@pytest.mark.asyncio(loop_scope="session")
async def test_sample_method_int(client):
    assert_golden("sample_method_int", await client._exec(IntegrationClient.run_sample_method_int))

@pytest.mark.asyncio(loop_scope="session")
async def test_sample_method_int_str(client):
    assert_golden("sample_method_int_str", await client._exec(IntegrationClient.run_sample_method_int_str))

@pytest.mark.asyncio(loop_scope="session")
async def test_sample_primitive_object_bool(client):
    assert_golden("sample_primitive_object_bool", await client._exec(IntegrationClient.run_sample_primitive_object_bool))

@pytest.mark.asyncio(loop_scope="session")
async def test_sample_primitive_object_int(client):
    assert_golden("sample_primitive_object_int", await client._exec(IntegrationClient.run_sample_primitive_object_int))

@pytest.mark.asyncio(loop_scope="session")
async def test_sample_primitive_object_string(client):
    assert_golden("sample_primitive_object_string", await client._exec(IntegrationClient.run_sample_primitive_object_string))

@pytest.mark.asyncio(loop_scope="session")
async def test_sample_vector3(client):
    assert_golden("sample_vector3", await client._exec(IntegrationClient.run_sample_vector3))

@pytest.mark.asyncio(loop_scope="session")
async def test_sample_vector3_array(client):
    assert_golden("sample_vector3_array", await client._exec(IntegrationClient.run_sample_vector3_array))

@pytest.mark.asyncio(loop_scope="session")
async def test_sample_vector3_dict(client):
    assert_golden("sample_vector3_dict", await client._exec(IntegrationClient.run_sample_vector3_dict))

@pytest.mark.asyncio(loop_scope="session")
async def test_sample_vector3_list(client):
    assert_golden("sample_vector3_list", await client._exec(IntegrationClient.run_sample_vector3_list))

@pytest.mark.asyncio(loop_scope="session")
async def test_set_abbreviated_classes(client):
    assert_golden("set_abbreviated_classes", await client._exec(IntegrationClient.run_set_abbreviated_classes))

@pytest.mark.asyncio(loop_scope="session")
async def test_set_abbreviated_classes_find(client):
    assert_golden("set_abbreviated_classes_find", await client._exec(IntegrationClient.run_set_abbreviated_classes_find))

@pytest.mark.asyncio(loop_scope="session")
async def test_set_transform_position(client):
    assert_golden("set_transform_position", await client._exec(IntegrationClient.run_set_transform_position))

@pytest.mark.asyncio(loop_scope="session")
async def test_transform_position(client):
    assert_golden("transform_position", await client._exec(IntegrationClient.run_transform_position))

@pytest.mark.asyncio(loop_scope="session")
async def test_transform_rotate(client):
    assert_golden("transform_rotate", await client._exec(IntegrationClient.run_transform_rotate))
